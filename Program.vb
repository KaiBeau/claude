Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

Module Program
    <STAThread>
    Sub Main()
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, AddressOf OnUiThreadException
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub

    Private Sub OnUiThreadException(sender As Object, e As ThreadExceptionEventArgs)
        ReportError(e.Exception)
    End Sub

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        ReportError(TryCast(e.ExceptionObject, Exception))
    End Sub

    ''' <summary>Shows the error instead of letting the app silently die, and writes it to a log file for later diagnosis.</summary>
    Private Sub ReportError(ex As Exception)
        Dim details = If(ex IsNot Nothing, ex.ToString(), "Unknown error (no exception details available).")
        Try
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "PdfFieldFiller-crash.log"), details)
        Catch
        End Try
        MessageBox.Show(details, "PDF Field Filler — unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub
End Module
