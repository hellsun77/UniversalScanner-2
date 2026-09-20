using System;
using System.IO;
using System.Windows.Forms;
using System.Text;

public class LoggerDump {
    public static void LogHierarchy(Form mainForm) {
        var sb = new StringBuilder();
        sb.AppendLine("=== LOG STARTED ===");
        sb.AppendLine($"Main Form ClientSize: {mainForm.ClientSize.Width}x{mainForm.ClientSize.Height}");
        
        void LogControl(Control c) {
            if (c == null) return;
            string type = c.GetType().Name;
            sb.AppendLine($"{type} '{c.Name}': Left={c.Left}, Top={c.Top}, Width={c.Width}, Height={c.Height}, Right={c.Right}, Bottom={c.Bottom}, Dock={c.Dock}, Anchor={c.Anchor}");
            
            if (c is DataGridView dgv) {
                sb.AppendLine($"  ColumnHeadersVisible={dgv.ColumnHeadersVisible}");
                sb.AppendLine($"  ColumnHeadersHeight={dgv.ColumnHeadersHeight}");
                sb.AppendLine($"  AutoSizeColumnsMode={dgv.AutoSizeColumnsMode}");
                sb.AppendLine($"  ScrollBars={dgv.ScrollBars}");
            }
            sb.AppendLine($"  MaximumSize={c.MaximumSize}, MinimumSize={c.MinimumSize}");
        }
        
        Control current = null;
        foreach (Control c in mainForm.Controls) {
            if (c is DataGridView) {
                current = c;
                break;
            }
        }
        
        while (current != null) {
            LogControl(current);
            current = current.Parent;
        }
        
        File.WriteAllText("ui_dump.txt", sb.ToString());
    }
}
