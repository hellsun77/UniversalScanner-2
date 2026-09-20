using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace UniversalScanner
{
    public partial class ScannerWindow
    {
        private void BuildExhaustiveProtocolsMenu(ToolStripMenuItem parentMenu)
        {
            parentMenu.DropDownItems.Clear();
            parentMenu.Text = "Exhaustive protocols...";
            
            // Remove previous handlers if any
            parentMenu.DropDownOpening -= ParentMenu_DropDownOpening;

            parentMenu.Click -= ParentMenu_Click;
            parentMenu.Click += ParentMenu_Click;
        }

        private void ParentMenu_Click(object sender, EventArgs e)
        {
            using (var form = new BrandFilterForm())
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    UpdateExhaustiveProtocolsMenuCheckmarks();
                    scanButton_Click(this, EventArgs.Empty);
                }
            }
        }

        private void ParentMenu_DropDownOpening(object sender, EventArgs e)
        {
            // Unused now
        }

        private HashSet<string> GetSelectedEngines()
        {
            var raw = Config.getInstance().EnabledDiscoveryEngines;
            if (string.IsNullOrEmpty(raw)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return new HashSet<string>(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        private void UpdateExhaustiveProtocolsMenuCheckmarks()
        {
            // In the new dialog-based design, the "Exhaustive protocols..." menu item 
            // itself does not need individual checkmarks for brands.
            // However, we can add a checkmark to the menu item itself to indicate if ANY filter is active,
            // or simply leave it unchecked. Let's just leave it empty or preserve its previous state.
            
            bool isAll = string.IsNullOrEmpty(Config.getInstance().EnabledDiscoveryEngines);
            
            if (exhaustiveprotocolsToolStripMenuItem != null)
            {
                exhaustiveprotocolsToolStripMenuItem.Checked = !isAll;
            }
        }
    }
}
