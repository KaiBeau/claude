Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Text
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports Microsoft.VisualBasic
Imports PdfSharp
Imports PdfSharp.Drawing
Imports PdfSharp.Pdf
Imports PdfSharp.Pdf.IO

Public Class MainForm
    Inherits Form

    Private WithEvents cmbPdf As ComboBox
    Private WithEvents btnBrowsePdf As Button
    Private WithEvents btnRemovePdf As Button
    Private WithEvents btnViewPdf As Button
    Private WithEvents btnMultiAddFields As Button
    Private lblPageInfo As Label

    Private WithEvents dgvFields As DataGridView
    Private WithEvents btnAddField As Button
    Private WithEvents btnRemoveField As Button
    Private WithEvents btnSaveLayout As Button
    Private WithEvents btnPrint As Button
    Private WithEvents btnGenerate As Button
    Private lblStatus As Label

    Private library As List(Of PdfLibraryEntry)
    Private suppressComboEvent As Boolean = False
    Private isSortingFields As Boolean = False
    Private dragRowIndex As Integer = -1

    ' Live, per-keystroke fit-checking while editing the Value column — see dgvFields_EditingControlShowing.
    Private valueEditBox As TextBox
    Private valueEditRowIndex As Integer = -1

    Private Const ColName As String = "colName"
    Private Const ColX As String = "colX"
    Private Const ColY As String = "colY"
    Private Const ColMaxX As String = "colMaxX"
    Private Const ColMaxY As String = "colMaxY"
    Private Const ColPage As String = "colPage"
    Private Const ColFont As String = "colFont"
    Private Const ColSize As String = "colSize"
    Private Const ColValue As String = "colValue"
    Private Const ColFixed As String = "colFixed"

    ''' <summary>Key SortFieldRows stashes a row's .Tag under inside its snapshot dictionary — not an
    ''' actual grid column, just a spare slot riding along in the same Dictionary(Of String, Object).</summary>
    Private Const RowTagKey As String = "__rowTag__"

    Private Const NotFixedText As String = "Not Fixed"
    Private Const SetText As String = "Set"
    Private Const FixedText As String = "Fixed"

    ''' <summary>A Value of literally this (case-insensitive, surrounding whitespace ignored) means "draw
    ''' a solid black box over this field's Max X/Max Y area instead of any text" — e.g. for redacting a
    ''' pre-printed box on the background PDF. Requires a box (Max X/Max Y); it defines the box's placement
    ''' and size the same way it does for auto-shrinking text.</summary>
    Private Const BlackBoxValue As String = "$BLACK"

    Private Shared Function IsBlackBoxValue(value As String) As Boolean
        Return String.Equals(If(value, "").Trim(), BlackBoxValue, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>A Value of literally this (case-insensitive, surrounding whitespace ignored) means "draw a
    ''' checkmark inside this field's Max X/Max Y area instead of any text" — e.g. for ticking a checkbox
    ''' on the background PDF. Works exactly like BlackBoxValue (requires the same box), just drawing a
    ''' tick mark scaled to fit it instead of a solid fill.</summary>
    Private Const TickValue As String = "$TICK"

    Private Shared Function IsTickValue(value As String) As Boolean
        Return String.Equals(If(value, "").Trim(), TickValue, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>A Value of literally this (case-insensitive, surrounding whitespace ignored) means "draw
    ''' nothing for this field" — e.g. to intentionally leave a position blank while still keeping the row
    ''' (and its position/box) defined in the layout. No box is required, since nothing is measured or drawn.</summary>
    Private Const EmptyValue As String = "$EMPTY"

    Private Shared Function IsEmptyValue(value As String) As Boolean
        Return String.Equals(If(value, "").Trim(), EmptyValue, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Preset choices offered via the Value column's right-click menu (see BuildValuePresetsMenu)
    ''' — add more here as needed. Picking one just sets the cell's Value to the plain preset string; the
    ''' column itself is ordinary free text, so this is purely a shortcut, never a restriction.</summary>
    Private Shared ReadOnly ValuePresets() As (Value As String, Description As String) = {
        (EmptyValue, "draw nothing"),
        (BlackBoxValue, "solid black box"),
        (TickValue, "checkmark")
    }

    Public Sub New()
        BuildUi()
    End Sub

    Private Sub BuildUi()
        Me.Text = "PDF Field Filler"
        Me.Width = 1120
        Me.Height = 650
        Me.StartPosition = FormStartPosition.CenterScreen

        ' --- Top panel: background PDF picker ---
        Dim topPanel As New Panel With {.Dock = DockStyle.Top, .Height = 70, .Padding = New Padding(8)}

        Dim lblPdf As New Label With {.Text = "Background PDF (optional) — previously used PDFs:", .AutoSize = True, .Location = New Point(8, 8)}
        cmbPdf = New ComboBox With {.Location = New Point(8, 28), .Width = 430, .DropDownStyle = ComboBoxStyle.DropDownList}
        btnBrowsePdf = New Button With {.Text = "Browse New PDF...", .Location = New Point(446, 26), .Width = 140}
        btnRemovePdf = New Button With {.Text = "Remove From List", .Location = New Point(592, 26), .Width = 140}
        btnViewPdf = New Button With {.Text = "View PDF", .Location = New Point(738, 26), .Width = 100, .Enabled = False}
        btnMultiAddFields = New Button With {.Text = "Add Multiple Fields...", .Location = New Point(844, 26), .Width = 160, .Enabled = False}
        lblPageInfo = New Label With {.Text = "No PDF loaded — blank A4 pages will be created.", .AutoSize = True, .Location = New Point(8, 52), .ForeColor = Color.DimGray}

        topPanel.Controls.AddRange({lblPdf, cmbPdf, btnBrowsePdf, btnRemovePdf, btnViewPdf, btnMultiAddFields, lblPageInfo})

        ' --- Instructions strip ---
        Dim lblHelp As New Label With {
            .Dock = DockStyle.Top,
            .Height = 62,
            .Padding = New Padding(8, 6, 8, 0),
            .Text = "Add one row per field. X/Y are in points measured from the TOP-LEFT of the page (72 points = 1 inch; A4 = 595 x 842 pt) and can't go negative or past that page's size — checked against the actual PDF page once one is loaded. Max X/Max Y are optional: set both to define a box (the field's far corner) and the font auto-shrinks (up to Size as a ceiling) so the value fits inside it. Without a box, Generate/Print check the value fits on the page at its exact font/size. Fill in every Value before generating. 'Set' locks a field's position/font (only Value stays editable) but keeps it with the variable fields. 'Fixed' locks everything, sinks the row to the bottom, and remembers its value for next time. Drag a row by its row header to reorder the variable fields. Select a field, click View PDF, then press down on any corner of its box and drag to the opposite corner — X/Y/Max X/Max Y are all set by pointing instead of typing. Add Multiple Fields... does the same thing repeatedly, adding a new field for each box until you click Done.",
            .ForeColor = Color.DimGray
        }

        ' --- Grid ---
        dgvFields = New DataGridView With {
            .Dock = DockStyle.Fill,
            .AllowUserToAddRows = True,
            .AllowUserToDeleteRows = True,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .RowHeadersWidth = 30,
            .AllowDrop = True
        }

        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColName, .HeaderText = "Field Name", .FillWeight = 16})
        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColX, .HeaderText = "X (pt)", .FillWeight = 8})
        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColY, .HeaderText = "Y (pt)", .FillWeight = 8})
        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColMaxX, .HeaderText = "Max X (pt)", .FillWeight = 9, .ToolTipText = "Right edge of the field's box (optional) — with Max Y, the font auto-shrinks to fit the value here."})
        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColMaxY, .HeaderText = "Max Y (pt)", .FillWeight = 9, .ToolTipText = "Bottom edge of the field's box (optional) — with Max X, the font auto-shrinks to fit the value here."})
        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColPage, .HeaderText = "Page #", .FillWeight = 8})

        Dim fontColumn As New DataGridViewComboBoxColumn With {
            .Name = ColFont,
            .HeaderText = "Font",
            .FillWeight = 15,
            .DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            .FlatStyle = FlatStyle.Flat
        }
        For Each familyName In GetInstalledFontNames()
            fontColumn.Items.Add(familyName)
        Next
        dgvFields.Columns.Add(fontColumn)

        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColSize, .HeaderText = "Size (pt)", .FillWeight = 9})

        ' Plain text, not a combo box — DataGridViewComboBoxColumn turned out to silently overwrite typed
        ' text with an Items entry under real mouse/keyboard use in ways that couldn't be reliably patched
        ' around. Presets are offered via the right-click menu below instead, which can't interfere with
        ' normal typing since it never touches the cell unless explicitly opened.
        dgvFields.Columns.Add(New DataGridViewTextBoxColumn With {.Name = ColValue, .HeaderText = "Value", .FillWeight = 32})
        dgvFields.Columns(ColValue).ContextMenuStrip = BuildValuePresetsMenu()

        Dim fixedColumn As New DataGridViewComboBoxColumn With {
            .Name = ColFixed,
            .HeaderText = "Fixed?",
            .FillWeight = 10,
            .DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            .FlatStyle = FlatStyle.Flat
        }
        fixedColumn.Items.AddRange({NotFixedText, SetText, FixedText})
        dgvFields.Columns.Add(fixedColumn)

        ' --- Bottom panel: buttons + status ---
        Dim bottomPanel As New Panel With {.Dock = DockStyle.Bottom, .Height = 60, .Padding = New Padding(8)}
        btnAddField = New Button With {.Text = "Add Field", .Location = New Point(8, 8), .Width = 100}
        btnRemoveField = New Button With {.Text = "Remove Selected", .Location = New Point(116, 8), .Width = 130}
        btnSaveLayout = New Button With {.Text = "Save Field Layout", .Location = New Point(260, 8), .Width = 150}
        btnPrint = New Button With {.Text = "Print", .Location = New Point(636, 8), .Width = 100, .Height = 34}
        btnGenerate = New Button With {.Text = "Generate PDF", .Location = New Point(750, 8), .Width = 150, .Height = 34}
        btnGenerate.Font = New Font(btnGenerate.Font, FontStyle.Bold)
        lblStatus = New Label With {.Text = "", .AutoSize = True, .Location = New Point(8, 40), .ForeColor = Color.DarkGreen}

        bottomPanel.Controls.AddRange({btnAddField, btnRemoveField, btnSaveLayout, btnPrint, btnGenerate, lblStatus})

        Me.Controls.Add(dgvFields)
        Me.Controls.Add(bottomPanel)
        Me.Controls.Add(lblHelp)
        Me.Controls.Add(topPanel)

        ' Selecting the default entry (Blank A4 Page) below loads its saved fields — or seeds the sample
        ' row itself, via LoadFieldsIntoGrid, if it has none yet — so no separate seeding call is needed here.
        LoadLibraryIntoCombo()
    End Sub

    Private Sub LoadLibraryIntoCombo()
        library = PdfLibrary.Load()

        ' "Blank A4 Page" (PdfPath = "") is a permanent, built-in choice — always present, always first,
        ' so picking "no background PDF" is an explicit, visible option rather than just leaving nothing
        ' selected. Its Fields still save/load like any other entry, as a reusable layout for PDF-less
        ' forms. Create it on first run, and re-pin it to the front if it's drifted anywhere else.
        Dim blankEntry = library.FirstOrDefault(Function(en) String.IsNullOrEmpty(en.PdfPath))
        If blankEntry Is Nothing Then
            blankEntry = New PdfLibraryEntry With {.PdfPath = ""}
            library.Insert(0, blankEntry)
        ElseIf library.IndexOf(blankEntry) <> 0 Then
            library.Remove(blankEntry)
            library.Insert(0, blankEntry)
        End If

        suppressComboEvent = True
        cmbPdf.Items.Clear()
        For Each entry In library
            cmbPdf.Items.Add(entry)
        Next
        suppressComboEvent = False

        ' "Blank A4 Page" is always index 0 — select it by default (not suppressed, so this runs the
        ' normal selection-changed handling below: page-info text and loading its saved fields, same as
        ' picking it from the dropdown by hand would).
        cmbPdf.SelectedIndex = 0
    End Sub

    ''' <summary>Right-click menu for the Value column, offering ValuePresets as quick picks — selecting
    ''' one just sets whichever cell was right-clicked (DataGridView makes that cell current before
    ''' showing a column's ContextMenuStrip) to the plain preset string, same as typing it by hand.</summary>
    Private Function BuildValuePresetsMenu() As ContextMenuStrip
        Dim menu As New ContextMenuStrip()
        For Each preset In ValuePresets
            Dim presetValue = preset.Value ' capture for the closure below
            Dim item = menu.Items.Add($"{preset.Value} — {preset.Description}")
            AddHandler item.Click, Sub()
                                        Dim cell = dgvFields.CurrentCell
                                        If cell Is Nothing OrElse cell.OwningColumn.Name <> ColValue Then Return
                                        If dgvFields.Rows(cell.RowIndex).IsNewRow Then Return
                                        cell.Value = presetValue
                                        RevalidateAllFieldFit()
                                    End Sub
        Next
        Return menu
    End Function

    Private Sub SeedSampleRow()
        Dim i = dgvFields.Rows.Add()
        dgvFields.Rows(i).Cells(ColName).Value = "FullName"
        dgvFields.Rows(i).Cells(ColX).Value = "72"
        dgvFields.Rows(i).Cells(ColY).Value = "100"
        dgvFields.Rows(i).Cells(ColPage).Value = "1"
        dgvFields.Rows(i).Cells(ColFont).Value = If(GetInstalledFontNames().Contains("Arial"), "Arial", GetInstalledFontNames().FirstOrDefault())
        dgvFields.Rows(i).Cells(ColSize).Value = "11"
        dgvFields.Rows(i).Cells(ColFixed).Value = NotFixedText
    End Sub

    Private Sub dgvFields_DefaultValuesNeeded(sender As Object, e As DataGridViewRowEventArgs) Handles dgvFields.DefaultValuesNeeded
        e.Row.Cells(ColFixed).Value = NotFixedText
        e.Row.Cells(ColPage).Value = "1"
        e.Row.Cells(ColSize).Value = "10"
        Dim defaultFont = GetFirstNonFixedFont()
        e.Row.Cells(ColFont).Value = If(defaultFont, If(GetInstalledFontNames().Contains("Arial"), "Arial", GetInstalledFontNames().FirstOrDefault()))
    End Sub

    Private Sub dgvFields_CurrentCellDirtyStateChanged(sender As Object, e As EventArgs) Handles dgvFields.CurrentCellDirtyStateChanged
        If dgvFields.IsCurrentCellDirty AndAlso TypeOf dgvFields.CurrentCell Is DataGridViewComboBoxCell Then
            dgvFields.CommitEdit(DataGridViewDataErrorContexts.Commit)
        End If
    End Sub

    ''' <summary>Rejects non-numeric or negative X/Y as soon as the user tries to leave the cell — the
    ''' page-size upper bound (and whether the value fits once rendered) is checked separately at
    ''' Generate/Print/Save Layout time, since that depends on the Page # and font too.</summary>
    Private Sub dgvFields_CellValidating(sender As Object, e As DataGridViewCellValidatingEventArgs) Handles dgvFields.CellValidating
        If e.RowIndex < 0 Then Return
        Dim colName2 = dgvFields.Columns(e.ColumnIndex).Name
        If colName2 <> ColX AndAlso colName2 <> ColY AndAlso colName2 <> ColMaxX AndAlso colName2 <> ColMaxY Then Return

        Dim text = Convert.ToString(e.FormattedValue)
        If String.IsNullOrWhiteSpace(text) Then
            dgvFields.Rows(e.RowIndex).Cells(e.ColumnIndex).ErrorText = ""
            Return
        End If

        Dim parsed As Double
        If Not Double.TryParse(text, parsed) OrElse parsed < 0 Then
            dgvFields.Rows(e.RowIndex).Cells(e.ColumnIndex).ErrorText = "Must be a number, 0 or greater (points)."
            e.Cancel = True
        Else
            dgvFields.Rows(e.RowIndex).Cells(e.ColumnIndex).ErrorText = ""
        End If
    End Sub

    Private Sub dgvFields_CellValueChanged(sender As Object, e As DataGridViewCellEventArgs) Handles dgvFields.CellValueChanged
        If isSortingFields Then Return ' ignore value changes we're making ourselves while rebuilding the grid below
        If e.RowIndex < 0 Then Return

        If dgvFields.Columns(e.ColumnIndex).Name = ColFixed Then
            ApplyRowLockState(dgvFields.Rows(e.RowIndex))

            ' Defer the re-sort via BeginInvoke so we never rebuild the Rows collection while still inside
            ' the cell-value-set call that triggered this event. BeginInvoke needs a created window handle,
            ' which doesn't exist yet during initial form construction — skip it there; LoadFieldsIntoGrid
            ' already sorts explicitly once its rows are built, so nothing is lost.
            If Me.IsHandleCreated Then
                Me.BeginInvoke(New MethodInvoker(AddressOf SortFieldRows))
            End If
        End If

        ' Any field's Name/X/Y/Max X/Max Y/Page/Font/Size/Value changing could change whether it fits —
        ' re-check every row, not just the one just edited, since editing is otherwise easy to miss.
        RevalidateAllFieldFit()
    End Sub

    ''' <summary>DataGridView reuses one editing control instance across cells, so every time a cell enters
    ''' edit mode this fires — unhook from whoever we were listening to before, and if the new cell is the
    ''' Value column, hook its TextBox's TextChanged so the fit color updates on every keystroke, not just
    ''' when the edit commits.</summary>
    Private Sub dgvFields_EditingControlShowing(sender As Object, e As DataGridViewEditingControlShowingEventArgs) Handles dgvFields.EditingControlShowing
        If valueEditBox IsNot Nothing Then
            RemoveHandler valueEditBox.TextChanged, AddressOf ValueEditBox_TextChanged
            valueEditBox = Nothing
            valueEditRowIndex = -1
        End If

        If dgvFields.CurrentCell Is Nothing OrElse dgvFields.CurrentCell.OwningColumn.Name <> ColValue Then Return
        Dim tb = TryCast(e.Control, TextBox)
        If tb Is Nothing Then Return

        valueEditBox = tb
        valueEditRowIndex = dgvFields.CurrentCell.RowIndex
        AddHandler valueEditBox.TextChanged, AddressOf ValueEditBox_TextChanged
    End Sub

    Private Sub ValueEditBox_TextChanged(sender As Object, e As EventArgs)
        If valueEditRowIndex < 0 OrElse valueEditRowIndex >= dgvFields.Rows.Count Then Return
        Dim row = dgvFields.Rows(valueEditRowIndex)
        If row.IsNewRow Then Return

        Dim pdfPath = GetSelectedPdfPath()
        Dim backgroundDoc As PdfDocument = Nothing
        Try
            If Not String.IsNullOrWhiteSpace(pdfPath) AndAlso File.Exists(pdfPath) Then
                Try
                    backgroundDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.InformationOnly)
                Catch
                    backgroundDoc = Nothing
                End Try
            End If
            ApplyValueFitColor(row, backgroundDoc, valueEditBox.Text)

            ' While a cell is actively being edited, what's on screen is the editing TextBox, not the
            ' cell's own painted style — DataGridView doesn't repaint from Style.ForeColor again until
            ' editing ends. Mirror the color onto the live control too, or the red flag never visibly
            ' appears until the user leaves the cell.
            valueEditBox.ForeColor = row.Cells(ColValue).Style.ForeColor
        Finally
            backgroundDoc?.Dispose()
        End Try
    End Sub

    Private Shared Function IsRowFixed(row As DataGridViewRow) As Boolean
        Return String.Equals(Convert.ToString(row.Cells(ColFixed).Value), FixedText, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function IsRowSet(row As DataGridViewRow) As Boolean
        Return String.Equals(Convert.ToString(row.Cells(ColFixed).Value), SetText, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Fixed locks every cell except Fixed?/Not Fixed/Set. Set locks everything except Value
    ''' (and that dropdown) — the position/font is settled but the value still varies per form. Not Fixed
    ''' leaves the whole row editable.</summary>
    Private Sub ApplyRowLockState(row As DataGridViewRow)
        Dim isFixed = IsRowFixed(row)
        Dim isSet = IsRowSet(row)
        Dim layoutLocked = isFixed OrElse isSet

        For Each cn In {ColName, ColX, ColY, ColMaxX, ColMaxY, ColPage, ColFont, ColSize}
            row.Cells(cn).ReadOnly = layoutLocked
            row.Cells(cn).Style.BackColor = If(isFixed, Color.Gainsboro, If(isSet, Color.LightGoldenrodYellow, Color.White))
        Next

        ' Value stays editable for Set (and Not Fixed) — only Fixed locks it too.
        row.Cells(ColValue).ReadOnly = isFixed
        row.Cells(ColValue).Style.BackColor = If(isFixed, Color.Gainsboro, Color.White)
    End Sub

    ''' <summary>Re-orders the grid so Not Fixed rows stay on top and Fixed rows sink to the bottom,
    ''' preserving relative order within each group. Also carries each row's .Tag through the rebuild
    ''' (see FindRowByTag) — without this, anything that clings to a DataGridViewRow reference or a tag
    ''' across this rebuild (e.g. Add Field handing a brand-new row off to the PDF picker) would silently
    ''' end up pointing at a detached, no-longer-visible row once this resort fires.</summary>
    Private Sub SortFieldRows()
        isSortingFields = True
        Try
            Dim columnNames = {ColName, ColX, ColY, ColMaxX, ColMaxY, ColPage, ColFont, ColSize, ColValue, ColFixed}
            Dim snapshot As New List(Of Dictionary(Of String, Object))
            For Each row As DataGridViewRow In dgvFields.Rows
                If row.IsNewRow Then Continue For
                Dim d As New Dictionary(Of String, Object)
                For Each cn In columnNames
                    d(cn) = row.Cells(cn).Value
                Next
                d(RowTagKey) = row.Tag
                snapshot.Add(d)
            Next

            Dim ordered = snapshot.OrderBy(Function(d) If(String.Equals(Convert.ToString(d(ColFixed)), FixedText, StringComparison.OrdinalIgnoreCase), 1, 0)).ToList()

            dgvFields.Rows.Clear()
            For Each d In ordered
                Dim i = dgvFields.Rows.Add()
                For Each cn In columnNames
                    dgvFields.Rows(i).Cells(cn).Value = d(cn)
                Next
                dgvFields.Rows(i).Tag = d(RowTagKey)
                ApplyRowLockState(dgvFields.Rows(i))
            Next
        Finally
            isSortingFields = False
        End Try
        RevalidateAllFieldFit()
    End Sub

    ''' <summary>Starts a row drag when the mouse goes down on the row header — never for a Fixed row
    ''' (they're locked and pinned to the bottom) or the trailing "add new row" placeholder.</summary>
    Private Sub dgvFields_MouseDown(sender As Object, e As MouseEventArgs) Handles dgvFields.MouseDown
        If e.Button <> MouseButtons.Left Then Return
        Dim hit = dgvFields.HitTest(e.X, e.Y)
        If hit.Type <> DataGridViewHitTestType.RowHeader OrElse hit.RowIndex < 0 Then Return

        Dim row = dgvFields.Rows(hit.RowIndex)
        If row.IsNewRow OrElse IsRowFixed(row) Then Return

        dragRowIndex = hit.RowIndex
        dgvFields.Rows(hit.RowIndex).Selected = True
        dgvFields.DoDragDrop(row, DragDropEffects.Move)
    End Sub

    Private Sub dgvFields_DragEnter(sender As Object, e As DragEventArgs) Handles dgvFields.DragEnter
        e.Effect = If(dragRowIndex >= 0, DragDropEffects.Move, DragDropEffects.None)
    End Sub

    Private Sub dgvFields_DragOver(sender As Object, e As DragEventArgs) Handles dgvFields.DragOver
        e.Effect = If(dragRowIndex >= 0, DragDropEffects.Move, DragDropEffects.None)
    End Sub

    Private Sub dgvFields_DragDrop(sender As Object, e As DragEventArgs) Handles dgvFields.DragDrop
        Try
            If dragRowIndex < 0 Then Return

            Dim clientPoint = dgvFields.PointToClient(New Point(e.X, e.Y))
            Dim hit = dgvFields.HitTest(clientPoint.X, clientPoint.Y)
            Dim targetIndex = hit.RowIndex
            If targetIndex < 0 Then targetIndex = dgvFields.Rows.Count - 1 ' dropped past the last row

            ' Never let a row land inside or below the Fixed block.
            Dim firstFixedIndex = GetFirstFixedRowIndex()
            If firstFixedIndex >= 0 AndAlso targetIndex >= firstFixedIndex Then
                targetIndex = firstFixedIndex - 1
            End If

            MoveRow(dragRowIndex, targetIndex)
        Finally
            dragRowIndex = -1
        End Try
    End Sub

    ''' <summary>Re-locates a row by the unique tag it was given at creation — needed after any operation
    ''' that rebuilds the Rows collection (SortFieldRows, MoveRow), since a held DataGridViewRow reference
    ''' from before that rebuild points at a detached row no longer part of the visible grid.</summary>
    Private Function FindRowByTag(tag As Object) As DataGridViewRow
        For Each row As DataGridViewRow In dgvFields.Rows
            If row.IsNewRow Then Continue For
            If Object.Equals(row.Tag, tag) Then Return row
        Next
        Return Nothing
    End Function

    Private Function GetFirstFixedRowIndex() As Integer
        For Each row As DataGridViewRow In dgvFields.Rows
            If row.IsNewRow Then Continue For
            If IsRowFixed(row) Then Return row.Index
        Next
        Return -1
    End Function

    Private Sub MoveRow(fromIndex As Integer, toIndex As Integer)
        If fromIndex < 0 OrElse fromIndex >= dgvFields.Rows.Count Then Return
        toIndex = Math.Max(0, toIndex)
        If fromIndex = toIndex Then Return

        isSortingFields = True ' suppress our own CellValueChanged handler while we move the row by hand
        Try
            Dim source = dgvFields.Rows(fromIndex)
            Dim values(source.Cells.Count - 1) As Object
            For i = 0 To source.Cells.Count - 1
                values(i) = source.Cells(i).Value
            Next
            Dim tag = source.Tag

            dgvFields.Rows.RemoveAt(fromIndex)
            If toIndex > fromIndex Then toIndex -= 1 ' shift target left to account for the removed row
            toIndex = Math.Min(toIndex, dgvFields.Rows.Count - 1) ' stay above the trailing new-row placeholder
            toIndex = Math.Max(toIndex, 0)

            dgvFields.Rows.Insert(toIndex, values)
            dgvFields.Rows(toIndex).Tag = tag
            ApplyRowLockState(dgvFields.Rows(toIndex))
            dgvFields.Rows(toIndex).Selected = True
        Finally
            isSortingFields = False
        End Try
        RevalidateAllFieldFit() ' re-inserting a row resets its style, so its fit color needs redoing too
    End Sub

    Private Shared Function GetInstalledFontNames() As List(Of String)
        Dim names As New List(Of String)
        Using ifc As New InstalledFontCollection()
            For Each ff In ifc.Families
                names.Add(ff.Name)
            Next
        End Using
        If names.Count = 0 Then names.Add("Arial")
        Return names
    End Function

    Private Function GetSelectedPdfPath() As String
        Dim entry = TryCast(cmbPdf.SelectedItem, PdfLibraryEntry)
        If entry Is Nothing Then Return String.Empty
        Return entry.PdfPath
    End Function

    Private Sub LoadFieldsIntoGrid(layouts As List(Of FieldLayout))
        dgvFields.Rows.Clear()
        For Each fl In layouts
            Dim i = dgvFields.Rows.Add()
            dgvFields.Rows(i).Cells(ColName).Value = fl.Name
            dgvFields.Rows(i).Cells(ColX).Value = fl.X.ToString()
            dgvFields.Rows(i).Cells(ColY).Value = fl.Y.ToString()
            If fl.MaxX.HasValue Then dgvFields.Rows(i).Cells(ColMaxX).Value = fl.MaxX.Value.ToString()
            If fl.MaxY.HasValue Then dgvFields.Rows(i).Cells(ColMaxY).Value = fl.MaxY.Value.ToString()
            dgvFields.Rows(i).Cells(ColPage).Value = fl.PageNumber.ToString()
            dgvFields.Rows(i).Cells(ColFont).Value = fl.FontName
            dgvFields.Rows(i).Cells(ColSize).Value = fl.FontSize.ToString()
            Dim state = If(String.IsNullOrEmpty(fl.FixedState), NotFixedText, fl.FixedState)
            dgvFields.Rows(i).Cells(ColFixed).Value = state
            ' A fixed field's value is remembered and pre-filled; Set and Not Fixed values are entered fresh each run.
            If String.Equals(state, FixedText, StringComparison.OrdinalIgnoreCase) Then dgvFields.Rows(i).Cells(ColValue).Value = fl.Value
            ApplyRowLockState(dgvFields.Rows(i))
        Next
        If layouts.Count = 0 Then SeedSampleRow()
        SortFieldRows()
    End Sub

    Private Sub UpdateViewPdfButtonState()
        Dim entry = TryCast(cmbPdf.SelectedItem, PdfLibraryEntry)
        btnViewPdf.Enabled = entry IsNot Nothing
        btnMultiAddFields.Enabled = entry IsNot Nothing
    End Sub

    Private Sub cmbPdf_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbPdf.SelectedIndexChanged
        If suppressComboEvent Then Return
        UpdateViewPdfButtonState()

        Dim entry = TryCast(cmbPdf.SelectedItem, PdfLibraryEntry)
        If entry Is Nothing Then Return

        If String.IsNullOrWhiteSpace(entry.PdfPath) Then
            ' The built-in "Blank A4 Page" choice — not a missing file, so no warning styling.
            lblPageInfo.Text = "No PDF loaded — blank A4 pages will be created."
            lblPageInfo.ForeColor = Color.DimGray
        ElseIf File.Exists(entry.PdfPath) Then
            Try
                Using doc = PdfReader.Open(entry.PdfPath, PdfDocumentOpenMode.InformationOnly)
                    lblPageInfo.Text = $"Loaded: {doc.PageCount} page(s). ({entry.PdfPath})"
                    lblPageInfo.ForeColor = Color.DarkGreen
                End Using
            Catch ex As Exception
                lblPageInfo.Text = "Could not open this PDF: " & ex.Message
                lblPageInfo.ForeColor = Color.Firebrick
            End Try
        Else
            lblPageInfo.Text = "File not found at its last known location — blank A4 pages will be used. (" & entry.PdfPath & ")"
            lblPageInfo.ForeColor = Color.Firebrick
        End If

        LoadFieldsIntoGrid(entry.Fields)
    End Sub

    Private Sub btnBrowsePdf_Click(sender As Object, e As EventArgs) Handles btnBrowsePdf.Click
        Using ofd As New OpenFileDialog With {.Filter = "PDF files (*.pdf)|*.pdf", .Title = "Select background PDF"}
            If ofd.ShowDialog() <> DialogResult.OK Then Return

            Dim existing = library.FirstOrDefault(Function(en) String.Equals(en.PdfPath, ofd.FileName, StringComparison.OrdinalIgnoreCase))
            If existing IsNot Nothing Then
                cmbPdf.SelectedItem = existing
                Return
            End If

            Try
                Using doc = PdfReader.Open(ofd.FileName, PdfDocumentOpenMode.InformationOnly)
                    lblPageInfo.Text = $"Loaded: {doc.PageCount} page(s)."
                    lblPageInfo.ForeColor = Color.DarkGreen
                End Using
            Catch ex As Exception
                MessageBox.Show("Could not open that PDF: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            Dim newEntry As New PdfLibraryEntry With {.PdfPath = ofd.FileName}
            library.Add(newEntry)
            PdfLibrary.Save(library)

            suppressComboEvent = True
            cmbPdf.Items.Add(newEntry)
            suppressComboEvent = False
            cmbPdf.SelectedItem = newEntry
        End Using
    End Sub

    Private Sub btnRemovePdf_Click(sender As Object, e As EventArgs) Handles btnRemovePdf.Click
        Dim entry = TryCast(cmbPdf.SelectedItem, PdfLibraryEntry)
        If entry Is Nothing Then
            MessageBox.Show("Select a PDF from the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        If String.IsNullOrWhiteSpace(entry.PdfPath) Then
            MessageBox.Show("'Blank A4 Page' is a built-in option and can't be removed.", "Can't remove", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim confirm = MessageBox.Show(
            $"Remove '{entry}' and its saved field layout from the list?" & vbCrLf & "(The PDF file itself is not deleted.)",
            "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If confirm <> DialogResult.Yes Then Return

        library.Remove(entry)
        PdfLibrary.Save(library)

        suppressComboEvent = True
        cmbPdf.Items.Remove(entry)
        cmbPdf.SelectedIndex = -1
        suppressComboEvent = False
        UpdateViewPdfButtonState()

        lblPageInfo.Text = "No PDF loaded — blank A4 pages will be created."
        lblPageInfo.ForeColor = Color.DimGray
        RevalidateAllFieldFit() ' page bounds just changed (back to A4), which can affect non-boxed fields' fit
    End Sub

    ''' <summary>Collects every row that already has a complete box (X/Y/Max X/Max Y all valid) for the
    ''' picker's ghost-box overlay — purely a display aid, nothing here reaches the generated PDF.</summary>
    Private Function GatherFieldBoxes(selectedRow As DataGridViewRow) As List(Of PickerFieldBox)
        Dim boxes As New List(Of PickerFieldBox)
        For Each row As DataGridViewRow In dgvFields.Rows
            If row.IsNewRow Then Continue For

            Dim x As Double, y As Double, maxX As Double, maxY As Double
            Dim hasBox = Double.TryParse(Convert.ToString(row.Cells(ColX).Value), x) AndAlso
                         Double.TryParse(Convert.ToString(row.Cells(ColY).Value), y) AndAlso
                         Double.TryParse(Convert.ToString(row.Cells(ColMaxX).Value), maxX) AndAlso
                         Double.TryParse(Convert.ToString(row.Cells(ColMaxY).Value), maxY) AndAlso
                         maxX > x AndAlso maxY > y
            If Not hasBox Then Continue For

            Dim pageNum As Integer
            If Not Integer.TryParse(Convert.ToString(row.Cells(ColPage).Value), pageNum) OrElse pageNum < 1 Then pageNum = 1

            boxes.Add(New PickerFieldBox With {
                .Name = Convert.ToString(row.Cells(ColName).Value),
                .X = x, .Y = y, .MaxX = maxX, .MaxY = maxY,
                .PageNumber = pageNum,
                .IsSelected = (row Is selectedRow)
            })
        Next
        Return boxes
    End Function

    Private Sub btnViewPdf_Click(sender As Object, e As EventArgs) Handles btnViewPdf.Click
        Dim row = dgvFields.CurrentRow
        If row Is Nothing OrElse row.IsNewRow Then
            MessageBox.Show("Select a field row in the grid first, then click View PDF to position it.", "No field selected", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        OpenPickerForRow(row, showErrors:=True)
    End Sub

    ''' <summary>Opens the click-to-position picker for the given row, if a usable background PDF is
    ''' loaded. showErrors controls whether a missing PDF/locked row pops up a message (the explicit View
    ''' PDF button) or is silently skipped (right after Add Field, where positioning by pointing is a
    ''' bonus on top of adding the field, not the point of the click). isNewField seeds Size (pt) from
    ''' the picked box's height and prompts for the field's Name once both corners are set (see
    ''' ApplyPickedPositionAndBox) — only for brand-new fields from Add Field, not when repositioning an
    ''' existing one via View PDF.</summary>
    Private Sub OpenPickerForRow(row As DataGridViewRow, showErrors As Boolean, Optional isNewField As Boolean = False)
        Dim fieldName = Convert.ToString(row.Cells(ColName).Value)
        If String.IsNullOrWhiteSpace(fieldName) Then fieldName = "(unnamed field)"

        If IsRowFixed(row) OrElse IsRowSet(row) Then
            If showErrors Then MessageBox.Show($"'{fieldName}' is locked (Fixed or Set) — switch it to Not Fixed first if you want to reposition it this way.", "Field is locked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim tempFileToCleanup As String = Nothing
        Dim pickerPdfPath = ResolvePickerPdfPath(showErrors, tempFileToCleanup)
        If pickerPdfPath Is Nothing Then Return

        Try
            Dim boxes = GatherFieldBoxes(row)
            Dim picker As New PdfPickerForm(pickerPdfPath, fieldName, boxes)

            ' The picker is non-modal, so the user could still drag-reorder rows in the main grid (or
            ' toggle another row's Fixed state) while it's open — either rebuilds dgvFields.Rows and would
            ' leave a directly-captured "row" reference pointing at a detached row. Tag it and re-find it
            ' by tag when the picker reports back instead of trusting the closed-over reference.
            If row.Tag Is Nothing Then row.Tag = Guid.NewGuid()
            Dim rowTag = row.Tag

            AddHandler picker.PositionAndBoxPicked, Sub(x As Double, y As Double, pageNumber As Integer, maxX As Double, maxY As Double)
                                                          Dim targetRow = FindRowByTag(rowTag)
                                                          If targetRow IsNot Nothing Then
                                                              ApplyPickedPositionAndBox(targetRow, x, y, pageNumber, maxX, maxY, setSizeFromHeight:=isNewField, promptForName:=isNewField)
                                                          End If
                                                      End Sub
            HookPickerTempFileCleanup(picker, tempFileToCleanup)
            picker.Show(Me)
        Catch ex As Exception
            If showErrors Then MessageBox.Show("Couldn't open this PDF for viewing: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Opens the picker in multi-add mode: every completed drag adds a brand-new row (same
    ''' defaults as Add Field) and positions it, then the picker resets for another drag instead of
    ''' closing — the user clicks Done in the picker itself when finished.</summary>
    Private Sub btnMultiAddFields_Click(sender As Object, e As EventArgs) Handles btnMultiAddFields.Click
        Dim tempFileToCleanup As String = Nothing
        Dim pickerPdfPath = ResolvePickerPdfPath(showErrors:=True, tempFileToCleanup)
        If pickerPdfPath Is Nothing Then Return

        Try
            ' No single row is "being positioned" here, so nothing starts out ghosted red — every
            ' pre-existing box (and, as they're added below, every new one from this session too) is
            ' just a plain yellow ghost.
            Dim boxes = GatherFieldBoxes(Nothing)
            Dim picker As New PdfPickerForm(pickerPdfPath, "each field", boxes, allowMultiple:=True)

            AddHandler picker.PositionAndBoxPicked, Sub(x As Double, y As Double, pageNumber As Integer, maxX As Double, maxY As Double)
                                                          Dim newRow = AddNewFieldRow()
                                                          If newRow Is Nothing Then Return
                                                          If newRow.Tag Is Nothing Then newRow.Tag = Guid.NewGuid()
                                                          Dim newRowTag = newRow.Tag

                                                          ApplyPickedPositionAndBox(newRow, x, y, pageNumber, maxX, maxY, setSizeFromHeight:=True, promptForName:=True)

                                                          ' promptForName's InputBox is a modal dialog — the grid could still get resorted
                                                          ' while it's open (e.g. from something else entirely), which would leave "newRow"
                                                          ' pointing at a detached row. Re-find it by tag rather than trust the closed-over
                                                          ' reference, same as OpenPickerForRow already does for the single-field flow.
                                                          Dim freshRow = FindRowByTag(newRowTag)
                                                          If freshRow Is Nothing Then Return

                                                          ' Show this just-added field as a ghost too, so the user can see where
                                                          ' everything from this session landed while picking the next one.
                                                          boxes.Add(New PickerFieldBox With {
                                                              .Name = Convert.ToString(freshRow.Cells(ColName).Value),
                                                              .X = x, .Y = y, .MaxX = maxX, .MaxY = maxY,
                                                              .PageNumber = pageNumber
                                                          })
                                                          picker.RefreshGhosts()
                                                      End Sub
            HookPickerTempFileCleanup(picker, tempFileToCleanup)
            picker.Show(Me)
        Catch ex As Exception
            MessageBox.Show("Couldn't open this PDF for viewing: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Resolves the picker's source PDF: the real selected file, or — when "Blank A4 Page" is
    ''' selected — a throwaway simulated multi-page blank A4 PDF, one page beyond whatever page number is
    ''' already used in the grid so there's always a further page to click onto. Shared by OpenPickerForRow
    ''' and btnMultiAddFields_Click. Returns Nothing on failure (a message was already shown if showErrors);
    ''' tempFileToCleanup is set only when a temp file was created and needs deleting once the picker closes
    ''' (see HookPickerTempFileCleanup).</summary>
    Private Function ResolvePickerPdfPath(showErrors As Boolean, ByRef tempFileToCleanup As String) As String
        tempFileToCleanup = Nothing

        Dim entry = TryCast(cmbPdf.SelectedItem, PdfLibraryEntry)
        If entry Is Nothing Then
            If showErrors Then MessageBox.Show("Select a background PDF (or Blank A4 Page) from the dropdown first.", "No PDF selected", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return Nothing
        End If

        If String.IsNullOrWhiteSpace(entry.PdfPath) Then
            ' No real file to show — simulate one. The picker just renders whatever path it's given, so
            ' it can't tell the difference.
            Dim tempFile = Path.Combine(Path.GetTempPath(), $"PdfFieldFiller_BlankA4_{Guid.NewGuid():N}.pdf")
            Try
                Using blankDoc = BuildBlankA4Document(GetMaxPageNumberInGrid() + 1)
                    blankDoc.Save(tempFile)
                End Using
            Catch ex As Exception
                If showErrors Then MessageBox.Show("Couldn't simulate a blank A4 page: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return Nothing
            End Try
            tempFileToCleanup = tempFile
            Return tempFile
        End If

        If Not File.Exists(entry.PdfPath) Then
            If showErrors Then MessageBox.Show("This PDF can't be found at its last known location:" & vbCrLf & entry.PdfPath, "File not found", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return Nothing
        End If

        Return entry.PdfPath
    End Function

    ''' <summary>Deletes tempFile once the picker is fully disposed — not FormClosed: PdfPickerForm's
    ''' OnFormClosed raises that event BEFORE it disposes its own pdfDoc (PdfiumViewer still has the file
    ''' open at that point), so deleting there would fail. Disposed fires strictly after Dispose() —
    ''' including that pdfDoc.Dispose() call — finishes, so the file is actually free by then. A no-op
    ''' when tempFile is Nothing (a real, non-simulated PDF was used).</summary>
    Private Sub HookPickerTempFileCleanup(picker As PdfPickerForm, tempFile As String)
        If tempFile Is Nothing Then Return
        AddHandler picker.Disposed, Sub()
                                         Try
                                             File.Delete(tempFile)
                                         Catch
                                             ' Best-effort cleanup of a temp file — leaving it behind isn't harmful.
                                         End Try
                                     End Sub
    End Sub

    ''' <summary>The highest Page # currently used across the grid's rows — 1 if none parse (empty grid,
    ''' or a brand-new unfilled row). Used to size the simulated Blank A4 document so there's always at
    ''' least one further page available to click onto beyond whatever's already placed.</summary>
    Private Function GetMaxPageNumberInGrid() As Integer
        Dim maxPage = 1
        For Each row As DataGridViewRow In dgvFields.Rows
            If row.IsNewRow Then Continue For
            Dim pageNum As Integer
            If Integer.TryParse(Convert.ToString(row.Cells(ColPage).Value), pageNum) AndAlso pageNum > maxPage Then
                maxPage = pageNum
            End If
        Next
        Return maxPage
    End Function

    ''' <summary>A throwaway, all-blank A4 PdfDocument with the given number of pages — used both to
    ''' simulate a page for the picker to render when "Blank A4 Page" is selected, and (via
    ''' BuildFilledDocument) as the base document Generate/Print actually fill in.</summary>
    Private Shared Function BuildBlankA4Document(pageCount As Integer) As PdfDocument
        Dim doc As New PdfDocument()
        For i = 1 To Math.Max(pageCount, 1)
            Dim p = doc.AddPage()
            p.Size = PageSize.A4
        Next
        Return doc
    End Function

    ''' <summary>Both picker corners are confirmed — write the position and box straight to the field row
    ''' that was selected when the picker opened. This runs from the picker's own closing timer, just
    ''' before it calls Me.Close() — so if promptForName pops up a modal dialog here, the picker window
    ''' is still visible behind it; it only actually closes once this handler returns.
    ''' setSizeFromHeight: for a brand-new field (Add Field), seed Size (pt) from the box's height
    ''' (Max Y - Y) instead of leaving whatever flat default it was created with — font size corresponds
    ''' to character height, not width, so this gives a ceiling that scales sensibly with the box rather
    ''' than one arbitrary number for every field. The auto-shrink at Generate time still caps the actual
    ''' rendered size to whatever the value needs.
    ''' promptForName: for a brand-new field, ask for its Name right here, right before the picker closes
    ''' — leaving it blank (or cancelling) just leaves the Name as it was, no error either way.</summary>
    Private Sub ApplyPickedPositionAndBox(row As DataGridViewRow, x As Double, y As Double, pageNumber As Integer, maxX As Double, maxY As Double, Optional setSizeFromHeight As Boolean = False, Optional promptForName As Boolean = False)
        row.Cells(ColX).Value = x.ToString("0.#")
        row.Cells(ColY).Value = y.ToString("0.#")
        row.Cells(ColPage).Value = pageNumber.ToString()
        row.Cells(ColMaxX).Value = maxX.ToString("0.#")
        row.Cells(ColMaxY).Value = maxY.ToString("0.#")

        If setSizeFromHeight Then
            Dim height = maxY - y
            If height > 0 Then row.Cells(ColSize).Value = height.ToString("0.#")
        End If

        If promptForName Then
            Dim currentName = Convert.ToString(row.Cells(ColName).Value)
            Dim enteredName = Interaction.InputBox("Enter a name for this field:", "Field Name", currentName)

            ' InputBox is modal and pumps its own nested message loop — on the off chance something
            ' resorts the grid while it's up, re-find this row by tag rather than trust "row" is still
            ' attached (same defensive pattern OpenPickerForRow already uses around this same dialog).
            If row.Tag IsNot Nothing Then
                Dim freshRow = FindRowByTag(row.Tag)
                If freshRow IsNot Nothing Then row = freshRow
            End If

            If Not String.IsNullOrWhiteSpace(enteredName) Then
                row.Cells(ColName).Value = enteredName.Trim()
            End If
        End If

        Dim fieldName = Convert.ToString(row.Cells(ColName).Value)
        If String.IsNullOrWhiteSpace(fieldName) Then fieldName = "(unnamed field)"
        lblStatus.Text = $"Position and box set for '{fieldName}'."
        lblStatus.ForeColor = Color.DarkGreen
    End Sub

    ''' <summary>The Font of the first row that isn't Fixed (i.e. Not Fixed or Set — the fields that stay
    ''' in the top, non-sunk group), so a newly added field starts out matching what's already in use
    ''' instead of falling back to whatever GetInstalledFontNames() happens to list first.</summary>
    Private Function GetFirstNonFixedFont() As String
        For Each row As DataGridViewRow In dgvFields.Rows
            If row.IsNewRow Then Continue For
            If IsRowFixed(row) Then Continue For
            Dim f = Convert.ToString(row.Cells(ColFont).Value)
            If Not String.IsNullOrWhiteSpace(f) Then Return f
        Next
        Return Nothing
    End Function

    ''' <summary>Adds a fresh, default-valued row (Page 1, Size 10, font inherited from the first Not
    ''' Fixed row) to the grid and returns it, already sorted into place — shared by Add Field and the
    ''' multi-field picker (btnMultiAddFields_Click), which both add rows this way and then position them
    ''' by pointing at the PDF.</summary>
    Private Function AddNewFieldRow() As DataGridViewRow
        Dim defaultFont = GetFirstNonFixedFont()

        Dim i = dgvFields.Rows.Add()
        Dim newRowTag As Guid = Guid.NewGuid()
        dgvFields.Rows(i).Tag = newRowTag
        dgvFields.Rows(i).Cells(ColPage).Value = "1"
        dgvFields.Rows(i).Cells(ColSize).Value = "10"
        dgvFields.Rows(i).Cells(ColFont).Value = If(defaultFont, If(GetInstalledFontNames().Contains("Arial"), "Arial", GetInstalledFontNames().FirstOrDefault()))

        ' Setting Fixed normally queues a *deferred* resort (dgvFields_CellValueChanged's BeginInvoke) —
        ' but SortFieldRows runs synchronously right below anyway, so that queued call would just be a
        ' redundant, LATE second resort landing at some arbitrary later point instead (even mid-way
        ' through a modal dialog a caller opens afterward, e.g. the picker's name prompt), rebuilding
        ' Rows again and invalidating whatever row reference that caller is still holding. isSortingFields
        ' suppresses CellValueChanged's reaction to this specific internal set — a brand-new "Not Fixed"
        ' row already renders unlocked/white by default, the same as ApplyRowLockState would set anyway,
        ' so skipping that side effect here changes nothing visible.
        isSortingFields = True
        dgvFields.Rows(i).Cells(ColFixed).Value = NotFixedText
        isSortingFields = False

        ' Force the resort to happen right now instead of on the next message-loop tick, so the tag
        ' lookup below is reliable and the grid is already settled before the row is used further.
        SortFieldRows()

        Return FindRowByTag(newRowTag) ' Nothing shouldn't happen, but callers guard anyway
    End Function

    Private Sub btnAddField_Click(sender As Object, e As EventArgs) Handles btnAddField.Click
        Dim newRow = AddNewFieldRow()
        If newRow Is Nothing Then Return ' shouldn't happen, but don't crash if it somehow does

        dgvFields.CurrentCell = newRow.Cells(ColName)

        ' If a background PDF is loaded, jump straight into positioning the new field by pointing at
        ' it — silently skipped (no pop-ups) when there's nothing to click on yet. Once both corners are
        ' picked, Size (pt) gets seeded from the box's height instead of staying at the flat "10" above.
        OpenPickerForRow(newRow, showErrors:=False, isNewField:=True)
    End Sub

    Private Sub btnRemoveField_Click(sender As Object, e As EventArgs) Handles btnRemoveField.Click
        For Each row As DataGridViewRow In dgvFields.SelectedRows.Cast(Of DataGridViewRow)().OrderByDescending(Function(r) r.Index)
            If Not row.IsNewRow Then dgvFields.Rows.Remove(row)
        Next
    End Sub

    ''' <summary>The page size to validate against when no background PDF (or no matching page in it) is
    ''' available — the same A4 size BuildFilledDocument pads blank/overflow pages with.</summary>
    Private Shared ReadOnly DefaultPageBounds As XSize = PageSizeConverter.ToSize(PageSize.A4)

    Private Shared Function GetPageBounds(backgroundDoc As PdfDocument, pageNumber As Integer) As XSize
        If backgroundDoc IsNot Nothing AndAlso pageNumber >= 1 AndAlso pageNumber <= backgroundDoc.PageCount Then
            Dim pg = backgroundDoc.Pages(pageNumber - 1)
            Return New XSize(pg.Width.Point, pg.Height.Point)
        End If
        Return DefaultPageBounds
    End Function

    ''' <summary>Measures how much space the value actually takes up in the given font/size, using PdfSharp's
    ''' own text-measurement context so it matches what will be drawn at render time.</summary>
    Private Shared Function MeasureTextSize(text As String, fontName As String, fontSize As Double) As XSize
        Dim font As New XFont(fontName, fontSize, XFontStyle.Regular)
        Using gfx = XGraphics.CreateMeasureContext(New XSize(5000, 5000), XGraphicsUnit.Point, XPageDirection.Downwards)
            Return gfx.MeasureString(text, font)
        End Using
    End Function

    Private Const MinFittedFontSize As Double = 4.0

    ''' <summary>The live warning's own, higher floor — separate from MinFittedFontSize, which stays the
    ''' true last-resort minimum Generate/Print will actually shrink to. Auto-shrink finding SOMETHING
    ''' that fits isn't the same as the value being reasonable: a 48-character value shrinking to 5.5pt in
    ''' a normal-sized box numerically "fits" but renders as unreadable garbage. The live check treats
    ''' anything that only fits by shrinking below this size as still worth flagging red, even though
    ''' Generate would go ahead and use it as a last resort.</summary>
    Private Const MinReadableFontSize As Double = 6.0

    ''' <summary>Slack allowed when comparing a measured/requested size against a box or page bound, so a
    ''' true exact fit isn't rejected by sub-point floating-point noise (from formatting X/Y/Max X/Max Y as
    ''' strings and re-parsing them, or from PdfSharp's own metrics) — most noticeable now that Add Field
    ''' seeds Size (pt) from the box's own width, which makes landing exactly on the boundary common.</summary>
    Private Const FitTolerance As Double = 0.05

    ''' <summary>The largest font size (in 0.5pt steps, up to maxSize) at which the value fits within a
    ''' boxWidth x boxHeight box — or Nothing if it doesn't fit even at MinFittedFontSize.</summary>
    Private Shared Function ComputeFittedFontSize(value As String, fontName As String, maxSize As Double, boxWidth As Double, boxHeight As Double) As Double?
        Dim size = maxSize
        While size >= MinFittedFontSize
            Dim measured = MeasureTextSize(value, fontName, size)
            If measured.Width <= boxWidth + FitTolerance AndAlso measured.Height <= boxHeight + FitTolerance Then Return size
            size -= 0.5
        End While
        Return Nothing
    End Function

    ''' <summary>Re-checks every field's Value against its box (or, without a box, the page) and colors
    ''' the Value cell red when it doesn't fit — a live warning, separate from the blocking checks at
    ''' Generate/Print/Save time. Cheap enough to run on every edit; opens the background PDF at most once.</summary>
    Private Sub RevalidateAllFieldFit()
        Dim pdfPath = GetSelectedPdfPath()
        Dim backgroundDoc As PdfDocument = Nothing
        Try
            If Not String.IsNullOrWhiteSpace(pdfPath) AndAlso File.Exists(pdfPath) Then
                Try
                    backgroundDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.InformationOnly)
                Catch
                    backgroundDoc = Nothing ' fall back to A4 bounds below if it can't be opened here
                End Try
            End If

            For Each row As DataGridViewRow In dgvFields.Rows
                If row.IsNewRow Then Continue For
                ApplyValueFitColor(row, backgroundDoc)
            Next
        Finally
            backgroundDoc?.Dispose()
        End Try
    End Sub

    ''' <summary>Sets one row's Value cell to red if its current Name/X/Y/Max X/Max Y/Page/Font/Size/Value
    ''' combination doesn't fit — black otherwise. For a boxed field this mirrors the auto-shrink Generate
    ''' actually does (red = won't fit even at the smallest usable size); for a non-boxed field, which has
    ''' no auto-shrink, it checks the exact configured Size. Any row with missing or unparsable data is
    ''' left black (not flagged) rather than guessed at, since that's usually just a field mid-edit.
    ''' liveValue, when given, overrides the cell's committed Value — used while the user is still typing
    ''' (see dgvFields_EditingControlShowing), since the cell's own Value doesn't update until commit.</summary>
    Private Sub ApplyValueFitColor(row As DataGridViewRow, backgroundDoc As PdfDocument, Optional liveValue As String = Nothing)
        Dim fits = True
        Dim value = If(liveValue, Convert.ToString(row.Cells(ColValue).Value))

        If Not String.IsNullOrEmpty(value) Then
            Dim x As Double, y As Double, fontSize As Double
            Dim xOk = Double.TryParse(Convert.ToString(row.Cells(ColX).Value), x)
            Dim yOk = Double.TryParse(Convert.ToString(row.Cells(ColY).Value), y)
            Dim sizeOk = Double.TryParse(Convert.ToString(row.Cells(ColSize).Value), fontSize)

            If xOk AndAlso yOk AndAlso sizeOk AndAlso fontSize > 0 Then
                Dim fontName = Convert.ToString(row.Cells(ColFont).Value)
                If String.IsNullOrWhiteSpace(fontName) Then fontName = "Arial"

                Dim pageNum As Integer
                If Not Integer.TryParse(Convert.ToString(row.Cells(ColPage).Value), pageNum) OrElse pageNum < 1 Then pageNum = 1

                Dim maxX As Double, maxY As Double
                Dim hasBox = Double.TryParse(Convert.ToString(row.Cells(ColMaxX).Value), maxX) AndAlso
                             Double.TryParse(Convert.ToString(row.Cells(ColMaxY).Value), maxY) AndAlso
                             maxX > x AndAlso maxY > y

                If IsEmptyValue(value) Then
                    ' Nothing gets drawn at all — always fits, box or no box.
                    fits = True
                ElseIf IsBlackBoxValue(value) OrElse IsTickValue(value) Then
                    ' No text to measure — this draws as a solid box or a tick mark, so "fits" just means
                    ' the box exists.
                    fits = hasBox
                ElseIf hasBox Then
                    ' Auto-shrink down to MinFittedFontSize looking for anything that fits — so red here
                    ' doesn't fire just because your configured Size happens to be bigger than the box
                    ' (routine, e.g. Add Field seeds Size from the box's own height, and gets resolved
                    ' automatically at render time). But "fits at 4-5pt" isn't the same as "looks fine" —
                    ' below MinReadableFontSize it's flagged too, even though Generate would still use it.
                    Dim fitted = ComputeFittedFontSize(value, fontName, fontSize, maxX - x, maxY - y)
                    fits = fitted.HasValue AndAlso fitted.Value >= MinReadableFontSize
                Else
                    Dim bounds = GetPageBounds(backgroundDoc, pageNum)
                    Dim textSize = MeasureTextSize(value, fontName, fontSize)
                    fits = x >= 0 AndAlso y >= 0 AndAlso (x + textSize.Width) <= bounds.Width + FitTolerance AndAlso (y + textSize.Height) <= bounds.Height + FitTolerance
                End If
            End If
        End If

        row.Cells(ColValue).Style.ForeColor = If(fits, Color.Black, Color.Red)
    End Sub

    ''' <summary>Reads and validates every grid row. Shows a message box and returns Nothing on the first problem.</summary>
    Private Function ValidateGridRows(requireValue As Boolean) As List(Of FieldDefinition)
        Dim fields As New List(Of FieldDefinition)
        Dim pdfPath = GetSelectedPdfPath()
        Dim backgroundDoc As PdfDocument = Nothing

        Try
            If Not String.IsNullOrWhiteSpace(pdfPath) AndAlso File.Exists(pdfPath) Then
                Try
                    backgroundDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.InformationOnly)
                Catch
                    backgroundDoc = Nothing ' fall back to A4 bounds below if it can't be opened here
                End Try
            End If

            For Each row As DataGridViewRow In dgvFields.Rows
                If row.IsNewRow Then Continue For

                Dim rowNum = row.Index + 1
                Dim name = Convert.ToString(row.Cells(ColName).Value).Trim()
                If String.IsNullOrEmpty(name) Then
                    MessageBox.Show($"Row {rowNum}: Field Name is required.", "Missing data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If

                Dim x As Double
                If Not Double.TryParse(Convert.ToString(row.Cells(ColX).Value), x) Then
                    MessageBox.Show($"Field '{name}': X must be a number (points).", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If

                Dim y As Double
                If Not Double.TryParse(Convert.ToString(row.Cells(ColY).Value), y) Then
                    MessageBox.Show($"Field '{name}': Y must be a number (points).", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If

                Dim pageNum As Integer
                If Not Integer.TryParse(Convert.ToString(row.Cells(ColPage).Value), pageNum) OrElse pageNum < 1 Then
                    MessageBox.Show($"Field '{name}': Page # must be a positive whole number.", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If

                Dim fontName = Convert.ToString(row.Cells(ColFont).Value)
                If String.IsNullOrWhiteSpace(fontName) Then fontName = "Arial"

                Dim fontSize As Double
                If Not Double.TryParse(Convert.ToString(row.Cells(ColSize).Value), fontSize) OrElse fontSize <= 0 Then
                    MessageBox.Show($"Field '{name}': Size must be a positive number (points).", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If

                Dim bounds = GetPageBounds(backgroundDoc, pageNum)
                If x < 0 OrElse x > bounds.Width + FitTolerance Then
                    MessageBox.Show($"Field '{name}': X must be between 0 and {bounds.Width:0.#} pt — page {pageNum} is {bounds.Width:0.#} pt wide.", "Position off the page", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If
                If y < 0 OrElse y > bounds.Height + FitTolerance Then
                    MessageBox.Show($"Field '{name}': Y must be between 0 and {bounds.Height:0.#} pt — page {pageNum} is {bounds.Height:0.#} pt tall.", "Position off the page", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If

                ' Max X/Max Y are optional — together they define a box whose far corner the value's
                ' font auto-shrinks to stay inside. Both or neither; never just one.
                Dim maxXText = Convert.ToString(row.Cells(ColMaxX).Value)
                Dim maxYText = Convert.ToString(row.Cells(ColMaxY).Value)
                Dim hasMaxX = Not String.IsNullOrWhiteSpace(maxXText)
                Dim hasMaxY = Not String.IsNullOrWhiteSpace(maxYText)
                If hasMaxX Xor hasMaxY Then
                    MessageBox.Show($"Field '{name}': Max X and Max Y must both be set together (or both left blank).", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return Nothing
                End If

                Dim maxX As Double? = Nothing
                Dim maxY As Double? = Nothing
                If hasMaxX Then
                    Dim mx As Double, my As Double
                    If Not Double.TryParse(maxXText, mx) OrElse Not Double.TryParse(maxYText, my) Then
                        MessageBox.Show($"Field '{name}': Max X and Max Y must be numbers (points).", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return Nothing
                    End If
                    If mx <= x Then
                        MessageBox.Show($"Field '{name}': Max X ({mx:0.#} pt) must be greater than X ({x:0.#} pt).", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return Nothing
                    End If
                    If my <= y Then
                        MessageBox.Show($"Field '{name}': Max Y ({my:0.#} pt) must be greater than Y ({y:0.#} pt).", "Invalid data", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return Nothing
                    End If
                    If mx > bounds.Width + FitTolerance OrElse my > bounds.Height + FitTolerance Then
                        MessageBox.Show($"Field '{name}': the box's far corner ({mx:0.#}, {my:0.#}) falls off page {pageNum} ({bounds.Width:0.#} x {bounds.Height:0.#} pt).", "Position off the page", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return Nothing
                    End If
                    maxX = mx
                    maxY = my
                End If

                Dim value = Convert.ToString(row.Cells(ColValue).Value)
                If requireValue AndAlso String.IsNullOrEmpty(value) Then
                    MessageBox.Show($"Field '{name}' still needs a value before you can generate the PDF.", "Missing value", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    dgvFields.CurrentCell = row.Cells(ColValue)
                    Return Nothing
                End If

                If IsEmptyValue(value) Then
                    ' Nothing gets drawn — no box or size requirement, unlike $BLACK/$TICK.
                ElseIf IsBlackBoxValue(value) OrElse IsTickValue(value) Then
                    ' Renders as a solid box or a tick mark, not text — the only requirement is that the
                    ' box itself (Max X/Max Y) is defined; its position was already checked against the
                    ' page above.
                    If Not maxX.HasValue OrElse Not maxY.HasValue Then
                        Dim presetName = If(IsTickValue(value), TickValue, BlackBoxValue)
                        MessageBox.Show($"Field '{name}': a value of '{presetName}' needs a Max X/Max Y box to define its placement and size.", "Missing box", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        dgvFields.CurrentCell = row.Cells(ColValue)
                        Return Nothing
                    End If
                ElseIf Not String.IsNullOrEmpty(value) Then
                    If maxX.HasValue AndAlso maxY.HasValue Then
                        Dim fitted = ComputeFittedFontSize(value, fontName, fontSize, maxX.Value - x, maxY.Value - y)
                        If fitted Is Nothing Then
                            MessageBox.Show(
                                $"Field '{name}': the value '{value}' doesn't fit in its box (X={x:0.#}..{maxX.Value:0.#}, Y={y:0.#}..{maxY.Value:0.#}) even at {MinFittedFontSize:0.#}pt, the minimum." & vbCrLf &
                                "Make the box bigger or shorten the value.",
                                "Value doesn't fit", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            dgvFields.CurrentCell = row.Cells(ColValue)
                            Return Nothing
                        End If
                    Else
                        Dim textSize = MeasureTextSize(value, fontName, fontSize)
                        If x + textSize.Width > bounds.Width + FitTolerance Then
                            MessageBox.Show(
                                $"Field '{name}': the value '{value}' at {fontSize:0.#}pt {fontName} is too wide to fit — it would run to {(x + textSize.Width):0.#} pt but page {pageNum} is only {bounds.Width:0.#} pt wide." & vbCrLf &
                                "Move it left, use a smaller font, shorten the value, or give it a Max X/Max Y box to auto-shrink into.",
                                "Value doesn't fit", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            dgvFields.CurrentCell = row.Cells(ColValue)
                            Return Nothing
                        End If
                        If y + textSize.Height > bounds.Height + FitTolerance Then
                            MessageBox.Show(
                                $"Field '{name}': the value at {fontSize:0.#}pt is too tall to fit — it would run to {(y + textSize.Height):0.#} pt but page {pageNum} is only {bounds.Height:0.#} pt tall." & vbCrLf &
                                "Move it up, use a smaller font, or give it a Max X/Max Y box to auto-shrink into.",
                                "Value doesn't fit", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            dgvFields.CurrentCell = row.Cells(ColValue)
                            Return Nothing
                        End If
                    End If
                End If

                Dim fixedState = Convert.ToString(row.Cells(ColFixed).Value)
                If String.IsNullOrEmpty(fixedState) Then fixedState = NotFixedText

                fields.Add(New FieldDefinition With {
                    .Name = name, .X = x, .Y = y, .MaxX = maxX, .MaxY = maxY, .PageNumber = pageNum,
                    .FontName = fontName, .FontSize = fontSize, .Value = value, .FixedState = fixedState
                })
            Next
            Return fields
        Finally
            backgroundDoc?.Dispose()
        End Try
    End Function

    Private Shared Function ToLayouts(fields As List(Of FieldDefinition)) As List(Of FieldLayout)
        Return fields.Select(Function(f) New FieldLayout With {
            .Name = f.Name, .X = f.X, .Y = f.Y, .MaxX = f.MaxX, .MaxY = f.MaxY, .PageNumber = f.PageNumber,
            .FontName = f.FontName, .FontSize = f.FontSize,
            .FixedState = f.FixedState,
            .Value = If(String.Equals(f.FixedState, FixedText, StringComparison.OrdinalIgnoreCase), f.Value, "")
        }).ToList()
    End Function

    Private Sub btnSaveLayout_Click(sender As Object, e As EventArgs) Handles btnSaveLayout.Click
        Dim entry = TryCast(cmbPdf.SelectedItem, PdfLibraryEntry)
        If entry Is Nothing Then
            MessageBox.Show("Select (or browse to) a PDF first — the field layout is saved per PDF.", "No PDF selected", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim fields = ValidateGridRows(requireValue:=False)
        If fields Is Nothing Then Return

        If String.IsNullOrWhiteSpace(entry.PdfPath) Then
            SaveBlankA4LayoutAsNewPdf(fields)
            Return
        End If

        entry.Fields = ToLayouts(fields)
        PdfLibrary.Save(library)
        lblStatus.Text = $"Field layout saved for {entry}."
        lblStatus.ForeColor = Color.DarkGreen
    End Sub

    ''' <summary>"Blank A4 Page" has no file behind it, so saving its field layout means turning the
    ''' simulated blank page into a real blank A4 PDF on disk first — the user is forced to pick a
    ''' filename (no default is pre-filled), that file is written as an actual blank A4 PDF, and it's
    ''' registered as a normal library entry carrying the current field layout, so it behaves exactly
    ''' like any browsed-to PDF from here on instead of staying tied to the generic placeholder.</summary>
    Private Sub SaveBlankA4LayoutAsNewPdf(fields As List(Of FieldDefinition))
        Dim maxPage = If(fields.Count > 0, fields.Max(Function(f) f.PageNumber), 1)

        Using sfd As New SaveFileDialog With {
            .Filter = "PDF files (*.pdf)|*.pdf",
            .FileName = "",
            .Title = "Blank A4 Page selected — enter a name for the new PDF"
        }
            If sfd.ShowDialog() <> DialogResult.OK Then Return

            Try
                Using blankDoc = BuildBlankA4Document(maxPage)
                    blankDoc.Save(sfd.FileName)
                End Using
            Catch ex As Exception
                MessageBox.Show("Couldn't save the blank A4 PDF: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            Dim newEntry = library.FirstOrDefault(Function(en) String.Equals(en.PdfPath, sfd.FileName, StringComparison.OrdinalIgnoreCase))
            If newEntry Is Nothing Then
                newEntry = New PdfLibraryEntry With {.PdfPath = sfd.FileName}
                library.Add(newEntry)
                suppressComboEvent = True
                cmbPdf.Items.Add(newEntry)
                suppressComboEvent = False
            End If
            newEntry.Fields = ToLayouts(fields)
            PdfLibrary.Save(library)

            ' Switches the dropdown to the new real entry — cmbPdf_SelectedIndexChanged reloads the grid
            ' from newEntry.Fields, which is what we just saved, so nothing the user sees actually changes.
            cmbPdf.SelectedItem = newEntry

            lblStatus.Text = $"Blank A4 saved as '{Path.GetFileName(sfd.FileName)}' and field layout saved."
            lblStatus.ForeColor = Color.DarkGreen
        End Using
    End Sub

    ''' <summary>Validates the grid and returns the fields to render, or Nothing if validation failed
    ''' (a message box was already shown) or there's nothing to do.</summary>
    Private Function GetFieldsToRender() As List(Of FieldDefinition)
        Dim fields = ValidateGridRows(requireValue:=True)
        If fields Is Nothing Then Return Nothing

        If fields.Count = 0 Then
            MessageBox.Show("Add at least one field first.", "Nothing to do", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return Nothing
        End If

        Return fields
    End Function

    ''' <summary>Builds the filled PDF: the selected background PDF (padded with blank A4 pages if needed)
    ''' or, with no background selected, blank A4 pages — with every field's value drawn at its position.</summary>
    Private Function BuildFilledDocument(fields As List(Of FieldDefinition)) As PdfDocument
        Dim pdfPath = GetSelectedPdfPath()
        Dim maxPage = fields.Max(Function(f) f.PageNumber)
        Dim doc As PdfDocument

        If Not String.IsNullOrWhiteSpace(pdfPath) AndAlso File.Exists(pdfPath) Then
            doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify)
            While doc.PageCount < maxPage
                Dim p = doc.AddPage()
                p.Size = PageSize.A4
            End While
        Else
            doc = BuildBlankA4Document(maxPage)
        End If

        For Each f In fields
            Dim page = doc.Pages(f.PageNumber - 1)

            If IsEmptyValue(f.Value) Then Continue For ' draw nothing for this field

            If IsBlackBoxValue(f.Value) AndAlso f.MaxX.HasValue AndAlso f.MaxY.HasValue Then
                ' Redaction-style solid box — the box (X/Y..MaxX/MaxY) defines its placement and size
                ' directly; no text, no font/auto-shrink involved.
                Using gfx = XGraphics.FromPdfPage(page)
                    Dim rect As New XRect(f.X, f.Y, f.MaxX.Value - f.X, f.MaxY.Value - f.Y)
                    gfx.DrawRectangle(XBrushes.Black, rect)
                End Using
                Continue For
            End If

            If IsTickValue(f.Value) AndAlso f.MaxX.HasValue AndAlso f.MaxY.HasValue Then
                ' Checkmark scaled to fit the box — same placement/size contract as $BLACK, just a tick
                ' instead of a solid fill.
                Using gfx = XGraphics.FromPdfPage(page)
                    DrawTickMark(gfx, f.X, f.Y, f.MaxX.Value, f.MaxY.Value)
                End Using
                Continue For
            End If

            ' With a box (MaxX/MaxY) defined, shrink the font just enough for THIS value to fit — the
            ' configured FontSize is the ceiling, never grown past, so a template stays predictable.
            Dim effectiveSize = f.FontSize
            Dim drawPoint = New XPoint(f.X, f.Y)
            Dim drawFormat = XStringFormats.TopLeft
            If f.MaxX.HasValue AndAlso f.MaxY.HasValue Then
                Dim fitted = ComputeFittedFontSize(f.Value, f.FontName, f.FontSize, f.MaxX.Value - f.X, f.MaxY.Value - f.Y)
                If fitted.HasValue Then effectiveSize = fitted.Value

                ' Anchor at the box's BOTTOM-left corner (the largest Y value — where the user clicked
                ' first) instead of its top, so the text sits low in the box and grows upward into it,
                ' rather than sitting at the top and printing too high on the page.
                drawPoint = New XPoint(f.X, f.MaxY.Value)
                drawFormat = XStringFormats.BottomLeft
            End If

            Using gfx = XGraphics.FromPdfPage(page)
                Dim font As New XFont(f.FontName, effectiveSize, XFontStyle.Regular)
                gfx.DrawString(f.Value, font, XBrushes.Black, drawPoint, drawFormat)
            End Using
        Next

        Return doc
    End Function

    ''' <summary>Draws a checkmark ("✓") inside the given box, in points — two strokes: a short one down
    ''' to the low point, a long one back up to the top-right, scaled and positioned proportionally to the
    ''' box so it looks right whether the box is small (a checkbox) or large.</summary>
    Private Shared Sub DrawTickMark(gfx As XGraphics, x As Double, y As Double, maxX As Double, maxY As Double)
        Dim w = maxX - x
        Dim h = maxY - y
        Dim penWidth = Math.Max(1.0, Math.Min(w, h) * 0.18)

        Dim p0 As New XPoint(x + w * 0.05, y + h * 0.55)
        Dim p1 As New XPoint(x + w * 0.40, y + h * 0.85)
        Dim p2 As New XPoint(x + w * 0.95, y + h * 0.12)

        Dim pen As New XPen(XColors.Black, penWidth)
        pen.LineCap = XLineCap.Round
        pen.LineJoin = XLineJoin.Round
        gfx.DrawLine(pen, p0, p1)
        gfx.DrawLine(pen, p1, p2)
    End Sub

    ''' <summary>Keeps the selected PDF's saved field layout in sync with whatever was just rendered.</summary>
    Private Sub SyncSelectedLayout(fields As List(Of FieldDefinition))
        Dim selEntry = TryCast(cmbPdf.SelectedItem, PdfLibraryEntry)
        If selEntry IsNot Nothing Then
            selEntry.Fields = ToLayouts(fields)
            PdfLibrary.Save(library)
        End If
    End Sub

    Private Sub btnGenerate_Click(sender As Object, e As EventArgs) Handles btnGenerate.Click
        lblStatus.Text = ""

        Dim fields = GetFieldsToRender()
        If fields Is Nothing Then Return

        Dim doc As PdfDocument = Nothing
        Try
            doc = BuildFilledDocument(fields)

            ' "Blank A4 Page" has no source file to name the output after — leave FileName blank
            ' so the Save dialog won't let the user click through with an unnamed/generic file.
            Dim isBlankA4 = String.IsNullOrWhiteSpace(GetSelectedPdfPath())
            Using sfd As New SaveFileDialog With {
                .Filter = "PDF files (*.pdf)|*.pdf",
                .FileName = If(isBlankA4, "", "Filled.pdf"),
                .Title = If(isBlankA4, "Blank A4 Page selected — enter a name for the new PDF", "Save filled PDF")
            }
                If sfd.ShowDialog() <> DialogResult.OK Then Return
                doc.Save(sfd.FileName)
                lblStatus.Text = "Saved: " & sfd.FileName
                lblStatus.ForeColor = Color.DarkGreen
                MessageBox.Show("PDF generated successfully.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End Using

            SyncSelectedLayout(fields)

        Catch ex As Exception
            MessageBox.Show("Failed to generate PDF: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            doc?.Dispose()
        End Try
    End Sub

    ''' <summary>Builds the filled PDF, saves it to a temp file, and hands it to the OS's registered
    ''' PDF handler to print — the same mechanism as right-clicking a PDF and choosing "Print". Some
    ''' viewers (Edge's built-in PDF viewer, notably) never register a "print" shell verb at all, even
    ''' when set as the default PDF app, so that attempt is expected to fail there — in which case we
    ''' just open the file instead and ask the user to print manually.</summary>
    Private Sub btnPrint_Click(sender As Object, e As EventArgs) Handles btnPrint.Click
        lblStatus.Text = ""

        Dim fields = GetFieldsToRender()
        If fields Is Nothing Then Return

        Dim doc As PdfDocument = Nothing
        Dim tempPath = Path.Combine(Path.GetTempPath(), $"PdfFieldFiller_{Guid.NewGuid():N}.pdf")
        Try
            doc = BuildFilledDocument(fields)
            doc.Save(tempPath)
        Catch ex As Exception
            MessageBox.Show("Failed to build the PDF: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        Finally
            doc?.Dispose()
        End Try

        Try
            Process.Start(New ProcessStartInfo(tempPath) With {
                .Verb = "print",
                .UseShellExecute = True,
                .WindowStyle = ProcessWindowStyle.Hidden
            })
            lblStatus.Text = "Sent to printer."
            lblStatus.ForeColor = Color.DarkGreen
        Catch
            ' No "print" verb registered for PDFs (common with Edge) — fall back to just opening the file.
            Try
                Process.Start(New ProcessStartInfo(tempPath) With {.UseShellExecute = True})
                lblStatus.Text = "Opened for printing — press Ctrl+P in the viewer."
                lblStatus.ForeColor = Color.DarkGreen
                MessageBox.Show(
                    "Your default PDF viewer doesn't support printing directly from other apps (this is a known limitation of Edge's built-in PDF viewer)." & vbCrLf & vbCrLf &
                    "The filled PDF has been opened instead — press Ctrl+P (or use the viewer's own Print button) to print it.",
                    "Opened for printing", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex2 As Exception
                MessageBox.Show(
                    "Couldn't open or print this PDF automatically." & vbCrLf & vbCrLf &
                    "The filled PDF was saved here so you can open and print it yourself:" & vbCrLf & tempPath & vbCrLf & vbCrLf &
                    "Error: " & ex2.Message,
                    "Couldn't print automatically", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End Try
        End Try

        SyncSelectedLayout(fields)
    End Sub

End Class
