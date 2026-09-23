Imports System.IO
Imports Newtonsoft.Json

''' <summary>A field's stored position/font for a given PDF template. Value is only persisted when
''' FixedState is "Fixed" — a fixed field's value doesn't change between forms, so it's remembered;
''' a "Not Fixed" or "Set" field's value is entered fresh each run.</summary>
Public Class FieldLayout
    Public Property Name As String = ""
    Public Property X As Double
    Public Property Y As Double
    ''' <summary>Bottom-right corner of the field's box, in points — optional (Nothing when not set).</summary>
    Public Property MaxX As Double?
    Public Property MaxY As Double?
    Public Property PageNumber As Integer = 1
    Public Property FontName As String = "Arial"
    Public Property FontSize As Double = 10
    ''' <summary>"Not Fixed", "Set", or "Fixed" — see MainForm's ApplyRowLockState for what each unlocks.</summary>
    Public Property FixedState As String = "Not Fixed"
    Public Property Value As String = ""
End Class

''' <summary>One remembered PDF and the field layout to reuse against it. An entry with an empty PdfPath
''' is the built-in "Blank A4 Page" placeholder (see MainForm.LoadLibraryIntoCombo) rather than a real
''' browsed-to file — its Fields still work normally, as a reusable layout for forms with no source PDF.</summary>
Public Class PdfLibraryEntry
    Public Property PdfPath As String = ""
    Public Property Fields As New List(Of FieldLayout)

    Public Overrides Function ToString() As String
        If String.IsNullOrEmpty(PdfPath) Then Return "Blank A4 Page"
        Return Path.GetFileName(PdfPath)
    End Function
End Class

''' <summary>Reads/writes the persisted list of previously used PDFs and their field layouts.</summary>
Public Class PdfLibrary
    Private Shared ReadOnly LibraryFolder As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfFieldFiller")

    Private Shared ReadOnly LibraryFile As String = Path.Combine(LibraryFolder, "library.json")

    Public Shared Function Load() As List(Of PdfLibraryEntry)
        Try
            If File.Exists(LibraryFile) Then
                Dim json = File.ReadAllText(LibraryFile)
                Dim list = JsonConvert.DeserializeObject(Of List(Of PdfLibraryEntry))(json)
                If list IsNot Nothing Then Return list
            End If
        Catch
            ' Corrupt or unreadable library file — start fresh rather than crash the app.
        End Try
        Return New List(Of PdfLibraryEntry)
    End Function

    Public Shared Sub Save(entries As List(Of PdfLibraryEntry))
        Directory.CreateDirectory(LibraryFolder)
        Dim json = JsonConvert.SerializeObject(entries, Formatting.Indented)
        File.WriteAllText(LibraryFile, json)
    End Sub
End Class
