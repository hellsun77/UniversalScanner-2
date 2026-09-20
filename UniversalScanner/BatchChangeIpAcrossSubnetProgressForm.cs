using System;
using System.Drawing;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class BatchChangeIpAcrossSubnetProgressForm : Form
    {
        private Label currentSubnetLabel;
        private Label currentBrandLabel;
        private Label currentCameraLabel;
        private Label successCountLabel;
        private Label failedCountLabel;
        private Label skippedCountLabel;
        private Label statusLabel;
        private Button cancelButton;
        private bool isCancelled;

        public bool IsCancelled => isCancelled;

        public BatchChangeIpAcrossSubnetProgressForm()
        {
            Text = "Batch Change IP Across Subnets Progress";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(500, 320);

            BuildUi();
        }

        private void BuildUi()
        {
            int leftLabel = 20;
            int leftValue = 180;
            int top = 20;
            int gap = 30;

            AddLabel("Current Subnet:", leftLabel, top);
            currentSubnetLabel = AddValueLabel("-", leftValue, top);

            top += gap;
            AddLabel("Current Brand:", leftLabel, top);
            currentBrandLabel = AddValueLabel("-", leftValue, top);

            top += gap;
            AddLabel("Current Camera:", leftLabel, top);
            currentCameraLabel = AddValueLabel("-", leftValue, top);

            top += gap;
            AddLabel("Success Count:", leftLabel, top);
            successCountLabel = AddValueLabel("0", leftValue, top);

            top += gap;
            AddLabel("Failed Count:", leftLabel, top);
            failedCountLabel = AddValueLabel("0", leftValue, top);

            top += gap;
            AddLabel("Skipped Count:", leftLabel, top);
            skippedCountLabel = AddValueLabel("0", leftValue, top);

            top += gap + 10;
            statusLabel = new Label
            {
                Left = leftLabel,
                Top = top,
                Width = 460,
                Height = 40,
                Text = "Starting..."
            };
            Controls.Add(statusLabel);

            top += 50;
            cancelButton = new Button
            {
                Text = "Abort",
                Left = 395,
                Top = top,
                Width = 85
            };
            cancelButton.Click += CancelButton_Click;
            Controls.Add(cancelButton);
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            isCancelled = true;
            cancelButton.Enabled = false;
            cancelButton.Text = "Aborting...";
        }

        private void AddLabel(string text, int left, int top)
        {
            Label label = new Label
            {
                Text = text,
                Left = left,
                Top = top,
                Width = 150,
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Bold)
            };
            Controls.Add(label);
        }

        private Label AddValueLabel(string text, int left, int top)
        {
            Label label = new Label
            {
                Text = text,
                Left = left,
                Top = top,
                Width = 300
            };
            Controls.Add(label);
            return label;
        }

        public void UpdateProgress(string currentSubnet, string currentBrand, string currentCamera, int successCount, int failedCount, int skippedCount, string statusMessage)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateProgress(currentSubnet, currentBrand, currentCamera, successCount, failedCount, skippedCount, statusMessage)));
                return;
            }

            if (currentSubnet != null) currentSubnetLabel.Text = currentSubnet;
            if (currentBrand != null) currentBrandLabel.Text = currentBrand;
            if (currentCamera != null) currentCameraLabel.Text = currentCamera;
            
            if (successCount >= 0) successCountLabel.Text = successCount.ToString();
            if (failedCount >= 0) failedCountLabel.Text = failedCount.ToString();
            if (skippedCount >= 0) skippedCountLabel.Text = skippedCount.ToString();
            
            if (statusMessage != null) statusLabel.Text = statusMessage;
            
            Application.DoEvents();
        }

        public void Finish()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(Finish));
                return;
            }

            cancelButton.Text = "Close";
            cancelButton.Enabled = true;
            cancelButton.Click -= CancelButton_Click;
            cancelButton.Click += (s, e) => Close();
        }
    }
}
