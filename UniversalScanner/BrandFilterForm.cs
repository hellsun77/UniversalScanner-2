using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class BrandFilterForm : Form
    {
        private CheckedListBox clbBrands;
        private Button btnClear;
        private Button btnOk;
        private Button btnCancel;

        private List<string> validEngines;
        private bool isUpdating = false;

        public BrandFilterForm()
        {
            InitializeComponent();
            PopulateBrands();
            LoadCurrentSelection();
            AppVisualTheme.Apply(this);
        }

        private void InitializeComponent()
        {
            this.clbBrands = new CheckedListBox();
            this.btnClear = new Button();
            this.btnOk = new Button();
            this.btnCancel = new Button();
            
            this.SuspendLayout();

            // clbBrands
            this.clbBrands.CheckOnClick = true;
            this.clbBrands.FormattingEnabled = true;
            this.clbBrands.Location = new Point(12, 12);
            this.clbBrands.Name = "clbBrands";
            this.clbBrands.Size = new Size(376, 401);
            this.clbBrands.TabIndex = 0;
            this.clbBrands.Font = new Font(this.Font.FontFamily, 10F, FontStyle.Regular);
            this.clbBrands.ItemCheck += new ItemCheckEventHandler(this.clbBrands_ItemCheck);

            // btnClear
            this.btnClear.Location = new Point(139, 425);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new Size(75, 23);
            this.btnClear.TabIndex = 2;
            this.btnClear.Text = "Clear";
            this.btnClear.UseVisualStyleBackColor = true;
            this.btnClear.Click += new EventHandler(this.btnClear_Click);

            // btnOk
            this.btnOk.Location = new Point(226, 425);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new Size(75, 23);
            this.btnOk.TabIndex = 3;
            this.btnOk.Text = "OK";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new EventHandler(this.btnOk_Click);

            // btnCancel
            this.btnCancel.Location = new Point(313, 425);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new Size(75, 23);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new EventHandler(this.btnCancel_Click);

            // BrandFilterForm
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(400, 460);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.clbBrands);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BrandFilterForm";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Scan Brands";
            
            this.ResumeLayout(false);
        }

        private void PopulateBrands()
        {
            validEngines = BrandEngineMapper.GetSupportedBrands().ToList();

            clbBrands.Items.Add("𝗔𝗹𝗹");
            foreach (var brand in validEngines)
            {
                clbBrands.Items.Add(brand);
            }
        }

        private void LoadCurrentSelection()
        {
            isUpdating = true;
            
            var raw = Config.getInstance().EnabledDiscoveryEngines;
            var isAll = string.IsNullOrEmpty(raw) || raw == "All";
            
            var currentSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!isAll)
            {
                currentSelection = new HashSet<string>(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            }

            for (int i = 0; i < clbBrands.Items.Count; i++)
            {
                string item = clbBrands.Items[i].ToString();
                if (i == 0)
                {
                    clbBrands.SetItemChecked(i, isAll);
                }
                else
                {
                    clbBrands.SetItemChecked(i, !isAll && currentSelection.Contains(item));
                }
            }

            if (!isAll)
            {
                bool allOthersChecked = true;
                for (int i = 1; i < clbBrands.Items.Count; i++)
                {
                    if (!clbBrands.GetItemChecked(i))
                    {
                        allOthersChecked = false;
                        break;
                    }
                }
                if (allOthersChecked)
                {
                    clbBrands.SetItemChecked(0, true);
                    for (int i = 1; i < clbBrands.Items.Count; i++)
                    {
                        clbBrands.SetItemChecked(i, false);
                    }
                }
            }

            isUpdating = false;
        }

        private void clbBrands_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (isUpdating) return;

            string selectedItem = clbBrands.Items[e.Index].ToString();
            bool willBeChecked = (e.NewValue == CheckState.Checked);

            BeginInvoke(new MethodInvoker(() =>
            {
                isUpdating = true;
                
                if (e.Index == 0) // "All"
                {
                    if (willBeChecked)
                    {
                        for (int i = 1; i < clbBrands.Items.Count; i++)
                        {
                            clbBrands.SetItemChecked(i, false);
                        }
                    }
                }
                else
                {
                    if (willBeChecked)
                    {
                        clbBrands.SetItemChecked(0, false);

                        bool allOthersChecked = true;
                        for (int i = 1; i < clbBrands.Items.Count; i++)
                        {
                            if (!clbBrands.GetItemChecked(i))
                            {
                                allOthersChecked = false;
                                break;
                            }
                        }
                        if (allOthersChecked)
                        {
                            clbBrands.SetItemChecked(0, true);
                            for (int i = 1; i < clbBrands.Items.Count; i++)
                            {
                                clbBrands.SetItemChecked(i, false);
                            }
                        }
                    }
                    else
                    {
                        clbBrands.SetItemChecked(0, false);
                    }
                }

                isUpdating = false;
            }));
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            isUpdating = true;
            for (int i = 0; i < clbBrands.Items.Count; i++)
            {
                clbBrands.SetItemChecked(i, false);
            }
            isUpdating = false;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Brand Filter] OK clicked");
            bool isAll = clbBrands.GetItemChecked(0);
            string savedValue = "";
            if (isAll)
            {
                Config.getInstance().EnabledDiscoveryEngines = "";
                savedValue = "All";
            }
            else
            {
                var selection = new List<string>();
                for (int i = 1; i < clbBrands.Items.Count; i++)
                {
                    if (clbBrands.GetItemChecked(i))
                    {
                        selection.Add(clbBrands.Items[i].ToString());
                    }
                }
                savedValue = string.Join(",", selection);
                Config.getInstance().EnabledDiscoveryEngines = savedValue;
            }
            
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Brand Filter] Saved selection: " + savedValue);
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Brand Filter] Setting DialogResult.OK");
            this.DialogResult = DialogResult.OK;
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Brand Filter] Closing dialog");
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
