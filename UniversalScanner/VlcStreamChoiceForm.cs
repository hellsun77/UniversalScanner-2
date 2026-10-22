using System;
using System.Drawing;
using System.Windows.Forms;

namespace UniversalScanner
{
    public enum VlcMosaicStreamChoice
    {
        Main,
        Sub
    }

    public class VlcStreamChoiceForm : Form
    {
        private RadioButton mainStreamRadio;
        private RadioButton subStreamRadio;

        public VlcMosaicStreamChoice SelectedStream
        {
            get { return subStreamRadio.Checked ? VlcMosaicStreamChoice.Sub : VlcMosaicStreamChoice.Main; }
        }

        public VlcStreamChoiceForm(int selectedCount)
        {
            Text = "Choose Stream";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 190);

            BuildUi(selectedCount);
            AppVisualTheme.Apply(this);
        }

        private void BuildUi(int selectedCount)
        {
            Label title = new Label();
            title.Left = 18;
            title.Top = 18;
            title.Width = 390;
            title.Height = 24;
            title.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            title.Text = "Selected cameras: " + selectedCount;
            Controls.Add(title);

            mainStreamRadio = new RadioButton();
            mainStreamRadio.Left = 24;
            mainStreamRadio.Top = 58;
            mainStreamRadio.Width = 370;
            mainStreamRadio.Text = "Main stream - best quality";
            Controls.Add(mainStreamRadio);

            subStreamRadio = new RadioButton();
            subStreamRadio.Left = 24;
            subStreamRadio.Top = 88;
            subStreamRadio.Width = 370;
            subStreamRadio.Text = "Sub stream - lighter for mosaic / many cameras";
            subStreamRadio.Checked = selectedCount > 4;
            mainStreamRadio.Checked = !subStreamRadio.Checked;
            Controls.Add(subStreamRadio);

            Button okButton = new Button();
            okButton.Text = "Open";
            okButton.Left = 248;
            okButton.Top = 140;
            okButton.Width = 78;
            okButton.DialogResult = DialogResult.OK;
            Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 336;
            cancelButton.Top = 140;
            cancelButton.Width = 78;
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }
    }
}
