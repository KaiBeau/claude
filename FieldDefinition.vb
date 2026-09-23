''' <summary>One field entered at runtime: its position/font on the page plus the value to print.</summary>
Public Class FieldDefinition
    Public Property Name As String = ""
    Public Property X As Double
    Public Property Y As Double
    ''' <summary>Bottom-right corner of the field's box, in points — optional. When both are set, the
    ''' rendered font size auto-shrinks (never grows past FontSize) so Value fits within X..MaxX and Y..MaxY.</summary>
    Public Property MaxX As Double?
    Public Property MaxY As Double?
    Public Property PageNumber As Integer = 1
    Public Property FontName As String = "Arial"
    ''' <summary>The preferred/maximum font size — the actual size used may be smaller if a box (MaxX/MaxY) is set and the value doesn't fit at this size.</summary>
    Public Property FontSize As Double = 10
    Public Property Value As String = ""
    ''' <summary>"Not Fixed", "Set", or "Fixed" — see MainForm's ApplyRowLockState for what each unlocks.</summary>
    Public Property FixedState As String = "Not Fixed"
End Class
