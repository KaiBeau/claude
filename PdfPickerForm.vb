Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Linq
Imports System.Windows.Forms
Imports PdfiumViewer

''' <summary>One already-defined field's box, in PDF points, for the picker's ghost-box overlay. Purely
''' informational — never drawn into the actual generated PDF.</summary>
Public Class PickerFieldBox
    Public Property Name As String = ""
    Public Property X As Double
    Public Property Y As Double
    Public Property MaxX As Double
    Public Property MaxY As Double
    Public Property PageNumber As Integer = 1
    Public Property IsSelected As Boolean = False
End Class

''' <summary>Renders a PDF page inside the app and walks the user through a single drag gesture for one
''' field's box: press the mouse down on ANY corner, drag, and release on the opposite corner — mouse-down
''' confirms the first corner, mouse-up confirms the second. The two points can be dragged in any
''' direction (top-left→bottom-right, bottom-right→top-left, etc.); whichever two corners are actually
''' used are normalized into the box's top-left/bottom-right before being reported back in a single event,
''' and this window then closes itself. Points are in PDF points (top-left origin, Y increasing downward —
''' matching how X/Y are used everywhere else in this app). While the page is shown, every other field
''' that already has a complete box is ghosted in translucent yellow so the user can see what's already
''' placed; the field currently being positioned (if it already had a box) is ghosted in red instead.</summary>
Public Class PdfPickerForm
    Inherits Form

    ''' <summary>Fired once, after both points are confirmed, right before this form closes itself.
    ''' x/y is the box's top-left corner (what the field renders from); maxX/maxY is its bottom-right.</summary>
    Public Event PositionAndBoxPicked(x As Double, y As Double, pageNumber As Integer, maxX As Double, maxY As Double)

    Private ReadOnly pdfDoc As PdfDocument
    Private ReadOnly fieldName As String
    Private ReadOnly existingBoxes As List(Of PickerFieldBox)

    ''' <summary>When true, a completed drag doesn't close this window — it reports the pick, resets back
    ''' to step 1 for another one, and the user clicks Done when finished. Used for adding several fields
    ''' in one session instead of just repositioning a single, already-selected one.</summary>
    Private ReadOnly allowMultiple As Boolean

    Private currentPageIndex As Integer = 0
    Private Const RenderDpi As Single = 120.0F
    Private ReadOnly pointsToPixels As Double = RenderDpi / 72.0

    ''' <summary>1 = picking the first corner (shows a crosshair once picked); 2 = picking the opposite
    ''' corner; 3 = both confirmed — showing the completed box briefly before closing.</summary>
    Private pickStep As Integer = 1
    Private anchorX, anchorY As Double
    Private anchorPage As Integer
    Private completedBox As PickerFieldBox

    ''' <summary>Live mouse position (in points) while picking the opposite corner, used to draw a
    ''' rubber-band preview box from the confirmed first corner out to the cursor.</summary>
    Private hasLiveMousePos As Boolean = False
    Private liveMouseX, liveMouseY As Double

    Private WithEvents btnPrevPage As Button
    Private WithEvents btnNextPage As Button
    Private WithEvents btnDone As Button
    Private lblPage As Label
    Private lblStep As Label
    Private WithEvents pictureBox As PictureBox
    Private scrollPanel As Panel
    Private WithEvents closeTimer As Timer

    Public Sub New(pdfPath As String, fieldName As String, existingBoxes As List(Of PickerFieldBox), Optional allowMultiple As Boolean = False)
        pdfDoc = PdfDocument.Load(pdfPath)
        Me.fieldName = fieldName
        Me.existingBoxes = existingBoxes
        Me.allowMultiple = allowMultiple
        BuildUi()
        RenderCurrentPage()
        UpdateStepLabel()
    End Sub

    Private Sub BuildUi()
        Me.Text = If(allowMultiple, "Add multiple fields", $"Drag to position '{fieldName}'")
        Me.Width = 900
        Me.Height = 800
        Me.StartPosition = FormStartPosition.CenterParent

        Dim topPanel As New Panel With {.Dock = DockStyle.Top, .Height = 90, .Padding = New Padding(8)}
        btnPrevPage = New Button With {.Text = "< Prev Page", .Location = New Point(8, 6), .Width = 100}
        btnNextPage = New Button With {.Text = "Next Page >", .Location = New Point(116, 6), .Width = 100}
        btnDone = New Button With {.Text = "Done", .Location = New Point(776, 6), .Width = 100}
        lblPage = New Label With {.Text = "Page 1", .AutoSize = True, .Location = New Point(228, 12), .Font = New Font(Me.Font, FontStyle.Bold)}
        lblStep = New Label With {
            .AutoSize = True,
            .Location = New Point(8, 40),
            .Font = New Font(Me.Font, FontStyle.Bold),
            .ForeColor = Color.DarkBlue
        }
        Dim lblHint As New Label With {
            .Text = If(allowMultiple,
                "Add as many fields as you like — click Done (above) when you're finished.",
                "This window closes itself once you release the drag."),
            .AutoSize = True,
            .Location = New Point(8, 64),
            .ForeColor = Color.DimGray
        }
        topPanel.Controls.AddRange({btnPrevPage, btnNextPage, btnDone, lblPage, lblStep, lblHint})

        scrollPanel = New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .BackColor = Color.DarkGray}
        pictureBox = New PictureBox With {.SizeMode = PictureBoxSizeMode.AutoSize, .Location = New Point(0, 0), .Cursor = Cursors.Cross}
        scrollPanel.Controls.Add(pictureBox)

        Me.Controls.Add(scrollPanel)
        Me.Controls.Add(topPanel)
    End Sub

    Private Sub UpdateStepLabel()
        If allowMultiple Then
            lblStep.Text =
                If(pickStep = 1, "Press the mouse down on any corner of the next field's box.",
                If(pickStep = 2, "Drag to the OPPOSITE corner and release.",
                "Field added — press down again for the next one, or click Done."))
        Else
            lblStep.Text =
                If(pickStep = 1, $"Press the mouse down on any corner of '{fieldName}''s box.",
                If(pickStep = 2, $"Drag to the OPPOSITE corner of '{fieldName}''s box and release.",
                $"'{fieldName}''s box is set — closing..."))
        End If
    End Sub

    ''' <summary>Lets the owner append to existingBoxes (the same List instance, held by reference) after
    ''' a pick and have the new ghost box actually appear — used in multi-add mode, where this window stays
    ''' open across several fields instead of closing after the first one.</summary>
    Public Sub RefreshGhosts()
        pictureBox.Invalidate()
    End Sub

    Private Sub btnDone_Click(sender As Object, e As EventArgs) Handles btnDone.Click
        Me.Close()
    End Sub

    Private Sub RenderCurrentPage()
        Dim oldImage = pictureBox.Image

        ' PdfiumViewer's Render(page, dpiX, dpiY, forPrinting) overload silently ignores the requested
        ' DPI and always renders at 1 pixel = 1 point — confirmed by direct testing. Compute the target
        ' pixel size ourselves from the page's actual point size and use the explicit width/height
        ' overload instead, so the image is really rendered at pointsToPixels scale as the click-handler
        ' below assumes.
        Dim pageSizePoints = pdfDoc.PageSizes(currentPageIndex)
        Dim pixelWidth = CInt(Math.Round(pageSizePoints.Width * pointsToPixels))
        Dim pixelHeight = CInt(Math.Round(pageSizePoints.Height * pointsToPixels))
        pictureBox.Image = pdfDoc.Render(currentPageIndex, pixelWidth, pixelHeight, RenderDpi, RenderDpi, False)
        oldImage?.Dispose()
        pictureBox.Invalidate() ' new image size means the ghost boxes (drawn in Paint) need redoing too

        lblPage.Text = $"Page {currentPageIndex + 1} of {pdfDoc.PageCount}"
        btnPrevPage.Enabled = currentPageIndex > 0
        btnNextPage.Enabled = currentPageIndex < pdfDoc.PageCount - 1
    End Sub

    ''' <summary>Draws a translucent box over every already-defined field on the CURRENT page — yellow for
    ''' others, red for the field being positioned right now (if it already had a box). Purely an on-screen
    ''' aid; nothing here is ever written into the generated PDF.</summary>
    Private Sub pictureBox_Paint(sender As Object, e As PaintEventArgs) Handles pictureBox.Paint
        Dim pageNumber = currentPageIndex + 1
        Dim onThisPage = existingBoxes.Where(Function(b) b.PageNumber = pageNumber)

        ' Draw the non-selected (yellow) boxes first, then the selected (red) one on top, so it's
        ' never hidden behind another field's box if they happen to overlap.
        For Each box In onThisPage.Where(Function(b) Not b.IsSelected)
            DrawGhostBox(e.Graphics, box, Color.Yellow, Color.FromArgb(200, Color.Goldenrod))
        Next
        For Each box In onThisPage.Where(Function(b) b.IsSelected)
            DrawGhostBox(e.Graphics, box, Color.Red, Color.FromArgb(220, Color.DarkRed))
        Next

        ' First corner is confirmed (mouse still down) and we're waiting on release — mark where it
        ' landed, and track the cursor with a live dashed preview of the box that releasing now would make.
        If pickStep = 2 AndAlso pageNumber = anchorPage Then
            DrawCrosshair(e.Graphics, anchorX, anchorY)
            If hasLiveMousePos Then
                DrawLivePreviewBox(e.Graphics, anchorX, anchorY, liveMouseX, liveMouseY)
            End If
        End If

        ' Both corners just confirmed — show the finished box in red briefly before this window closes.
        If pickStep = 3 AndAlso completedBox IsNot Nothing AndAlso pageNumber = completedBox.PageNumber Then
            DrawGhostBox(e.Graphics, completedBox, Color.Red, Color.FromArgb(220, Color.DarkRed))
        End If
    End Sub

    Private Sub DrawGhostBox(g As Graphics, box As PickerFieldBox, fillColor As Color, borderColor As Color)
        Dim rectX = CInt(Math.Round(box.X * pointsToPixels))
        Dim rectY = CInt(Math.Round(box.Y * pointsToPixels))
        Dim rectW = CInt(Math.Round((box.MaxX - box.X) * pointsToPixels))
        Dim rectH = CInt(Math.Round((box.MaxY - box.Y) * pointsToPixels))
        If rectW <= 0 OrElse rectH <= 0 Then Return
        Dim rect As New Rectangle(rectX, rectY, rectW, rectH)

        Using brush As New SolidBrush(Color.FromArgb(70, fillColor))
            g.FillRectangle(brush, rect)
        End Using
        Using pen As New Pen(borderColor, 2)
            g.DrawRectangle(pen, rect)
        End Using
    End Sub

    ''' <summary>Marks the just-confirmed first corner with a crosshair — a white halo behind a
    ''' blue "+" and a small ring, so it stays visible over both light and dark parts of the page.</summary>
    Private Sub DrawCrosshair(g As Graphics, xPoints As Double, yPoints As Double)
        Dim px = CInt(Math.Round(xPoints * pointsToPixels))
        Dim py = CInt(Math.Round(yPoints * pointsToPixels))
        Const arm As Integer = 14
        Const ringRadius As Integer = 5

        Using halo As New Pen(Color.White, 4)
            g.DrawLine(halo, px - arm, py, px + arm, py)
            g.DrawLine(halo, px, py - arm, px, py + arm)
        End Using
        Using pen As New Pen(Color.Blue, 2)
            g.DrawLine(pen, px - arm, py, px + arm, py)
            g.DrawLine(pen, px, py - arm, px, py + arm)
            g.DrawEllipse(pen, px - ringRadius, py - ringRadius, ringRadius * 2, ringRadius * 2)
        End Using
    End Sub

    ''' <summary>The live rubber-band preview between the pressed-down first corner and wherever the mouse
    ''' is right now — dashed, to read as "still dragging" rather than the solid-bordered confirmed boxes.
    ''' Always draws the bounding rectangle between the two points — any drag direction is valid now, so
    ''' there's nothing to reject on release.</summary>
    Private Sub DrawLivePreviewBox(g As Graphics, x1 As Double, y1 As Double, x2 As Double, y2 As Double)
        Dim rectX = CInt(Math.Round(Math.Min(x1, x2) * pointsToPixels))
        Dim rectY = CInt(Math.Round(Math.Min(y1, y2) * pointsToPixels))
        Dim rectW = CInt(Math.Round(Math.Abs(x2 - x1) * pointsToPixels))
        Dim rectH = CInt(Math.Round(Math.Abs(y2 - y1) * pointsToPixels))
        If rectW <= 0 OrElse rectH <= 0 Then Return
        Dim rect As New Rectangle(rectX, rectY, rectW, rectH)

        Using brush As New SolidBrush(Color.FromArgb(60, Color.Red))
            g.FillRectangle(brush, rect)
        End Using
        Using pen As New Pen(Color.Red, 2)
            pen.DashStyle = DashStyle.Dash
            g.DrawRectangle(pen, rect)
        End Using
    End Sub

    Private Sub btnPrevPage_Click(sender As Object, e As EventArgs) Handles btnPrevPage.Click
        If currentPageIndex > 0 Then
            currentPageIndex -= 1
            RenderCurrentPage()
        End If
    End Sub

    Private Sub btnNextPage_Click(sender As Object, e As EventArgs) Handles btnNextPage.Click
        If currentPageIndex < pdfDoc.PageCount - 1 Then
            currentPageIndex += 1
            RenderCurrentPage()
        End If
    End Sub

    ''' <summary>Tracks the cursor while we're waiting on the opposite corner so the Paint handler can
    ''' draw a live rubber-band preview out to wherever the mouse currently is.</summary>
    Private Sub pictureBox_MouseMove(sender As Object, e As MouseEventArgs) Handles pictureBox.MouseMove
        If pickStep <> 2 Then Return
        liveMouseX = e.X / pointsToPixels
        liveMouseY = e.Y / pointsToPixels
        hasLiveMousePos = True
        pictureBox.Invalidate()
    End Sub

    Private Sub pictureBox_MouseLeave(sender As Object, e As EventArgs) Handles pictureBox.MouseLeave
        If Not hasLiveMousePos Then Return
        hasLiveMousePos = False
        pictureBox.Invalidate() ' don't leave a stale preview box hanging once the cursor's gone
    End Sub

    ''' <summary>Mouse-down IS the confirmation of the first corner — no pop-up, no separate click. Any
    ''' corner of the intended box is fine; MouseUp below works out which two points were actually used.
    ''' Captures the mouse so the eventual MouseUp still reaches this control even if the drag strays
    ''' outside the picture box's bounds (otherwise a release outside it would never fire at all, leaving
    ''' the picker stuck waiting forever).</summary>
    Private Sub pictureBox_MouseDown(sender As Object, e As MouseEventArgs) Handles pictureBox.MouseDown
        If e.Button <> MouseButtons.Left Then Return
        If pickStep <> 1 Then Return ' already dragging, or both corners are set and we're closing

        anchorX = e.X / pointsToPixels
        anchorY = e.Y / pointsToPixels
        anchorPage = currentPageIndex + 1
        pickStep = 2
        UpdateStepLabel()
        pictureBox.Invalidate() ' show the crosshair at the just-confirmed corner
        pictureBox.Capture = True
    End Sub

    ''' <summary>Mouse-up IS the confirmation of the second corner (if it lands somewhere valid) — no
    ''' pop-up. Any drag direction is accepted; the two points are normalized into the box's top-left/
    ''' bottom-right below rather than requiring a specific bottom-left-to-top-right gesture. An invalid
    ''' release (wrong page, or the same point as the first corner) just cancels this drag rather than
    ''' looping back for a retry, matching how a rubber-band drag normally behaves elsewhere: press down
    ''' again to start over.</summary>
    Private Sub pictureBox_MouseUp(sender As Object, e As MouseEventArgs) Handles pictureBox.MouseUp
        If e.Button <> MouseButtons.Left Then Return
        If pickStep <> 2 Then Return ' nothing was in progress

        pictureBox.Capture = False

        Dim x = e.X / pointsToPixels
        Dim y = e.Y / pointsToPixels
        Dim pageNumber = currentPageIndex + 1

        If pageNumber <> anchorPage Then
            MessageBox.Show("The second corner has to be on the same page as the first — press down again to retry.", "Wrong page", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            CancelDrag()
            Return
        End If

        ' Any corner could have been pressed first, so normalize whichever two points were actually
        ' dragged into the box's top-left (smallest X/Y) and bottom-right (largest X/Y) — Y increases
        ' downward here, same as everywhere else X/Y are used in this app.
        Dim fieldX = Math.Min(anchorX, x)
        Dim fieldY = Math.Min(anchorY, y)
        Dim maxX = Math.Max(anchorX, x)
        Dim maxY = Math.Max(anchorY, y)

        If maxX <= fieldX OrElse maxY <= fieldY Then
            MessageBox.Show("The second corner must be somewhere other than the first — press down again to retry.", "Invalid corner", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            CancelDrag()
            Return
        End If

        ' Swap the crosshair for the finished red box and let the user see it briefly before closing.
        completedBox = New PickerFieldBox With {.X = fieldX, .Y = fieldY, .MaxX = maxX, .MaxY = maxY, .PageNumber = anchorPage, .IsSelected = True}
        pickStep = 3
        UpdateStepLabel()
        pictureBox.Invalidate()

        closeTimer = New Timer With {.Interval = 600}
        AddHandler closeTimer.Tick, Sub()
                                         closeTimer.Stop()
                                         RaiseEvent PositionAndBoxPicked(fieldX, fieldY, anchorPage, maxX, maxY)
                                         If allowMultiple Then
                                             ' Stay open — reset for another drag instead of closing. The
                                             ' just-finished box becomes a permanent yellow ghost (via the
                                             ' owner's RefreshGhosts call after it adds it to existingBoxes),
                                             ' not the transient red "just completed" one anymore.
                                             completedBox = Nothing
                                             pickStep = 1
                                             UpdateStepLabel()
                                             pictureBox.Invalidate()
                                         Else
                                             Me.Close()
                                         End If
                                     End Sub
        closeTimer.Start()
    End Sub

    ''' <summary>Resets back to "waiting for the first corner" after an invalid release.</summary>
    Private Sub CancelDrag()
        pickStep = 1
        hasLiveMousePos = False
        UpdateStepLabel()
        pictureBox.Invalidate()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        MyBase.OnFormClosed(e)
        closeTimer?.Stop()
        closeTimer?.Dispose()
        pictureBox.Image?.Dispose()
        pdfDoc.Dispose()
    End Sub

End Class
