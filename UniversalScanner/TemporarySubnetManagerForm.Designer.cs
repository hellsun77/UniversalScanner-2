namespace UniversalScanner
{
    partial class TemporarySubnetManagerForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListView listView;
        private System.Windows.Forms.ColumnHeader colAdapter;
        private System.Windows.Forms.ColumnHeader colTempIp;
        private System.Windows.Forms.ColumnHeader colMask;
        private System.Windows.Forms.ColumnHeader colCameraIp;
        private System.Windows.Forms.ColumnHeader colStatus;
        private System.Windows.Forms.ColumnHeader colCreated;
        private System.Windows.Forms.Button btnRemoveSelected;
        private System.Windows.Forms.Button btnRemoveAll;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.CheckBox chkAutoRemove;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.listView = new System.Windows.Forms.ListView();
            this.colAdapter = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colTempIp = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colMask = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colCameraIp = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colStatus = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colCreated = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.btnRemoveSelected = new System.Windows.Forms.Button();
            this.btnRemoveAll = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.chkAutoRemove = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // listView
            // 
            this.listView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.listView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colAdapter,
            this.colTempIp,
            this.colMask,
            this.colCameraIp,
            this.colStatus,
            this.colCreated});
            this.listView.FullRowSelect = true;
            this.listView.HideSelection = false;
            this.listView.Location = new System.Drawing.Point(12, 12);
            this.listView.Name = "listView";
            this.listView.Size = new System.Drawing.Size(710, 260);
            this.listView.TabIndex = 0;
            this.listView.UseCompatibleStateImageBehavior = false;
            this.listView.View = System.Windows.Forms.View.Details;
            // 
            // colAdapter
            // 
            this.colAdapter.Text = "Network Adapter";
            this.colAdapter.Width = 120;
            // 
            // colTempIp
            // 
            this.colTempIp.Text = "Temporary IPv4";
            this.colTempIp.Width = 110;
            // 
            // colMask
            // 
            this.colMask.Text = "Subnet Mask";
            this.colMask.Width = 100;
            // 
            // colCameraIp
            // 
            this.colCameraIp.Text = "Camera IP";
            this.colCameraIp.Width = 100;
            // 
            // colStatus
            // 
            this.colStatus.Text = "Status";
            this.colStatus.Width = 100;
            // 
            // colCreated
            // 
            this.colCreated.Text = "Time Created";
            this.colCreated.Width = 150;
            // 
            // btnRemoveSelected
            // 
            this.btnRemoveSelected.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRemoveSelected.Location = new System.Drawing.Point(12, 283);
            this.btnRemoveSelected.Name = "btnRemoveSelected";
            this.btnRemoveSelected.Size = new System.Drawing.Size(120, 30);
            this.btnRemoveSelected.TabIndex = 1;
            this.btnRemoveSelected.Text = "Remove Selected";
            this.btnRemoveSelected.UseVisualStyleBackColor = true;
            this.btnRemoveSelected.Click += new System.EventHandler(this.btnRemoveSelected_Click);
            // 
            // btnRemoveAll
            // 
            this.btnRemoveAll.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRemoveAll.Location = new System.Drawing.Point(138, 283);
            this.btnRemoveAll.Name = "btnRemoveAll";
            this.btnRemoveAll.Size = new System.Drawing.Size(100, 30);
            this.btnRemoveAll.TabIndex = 2;
            this.btnRemoveAll.Text = "Remove All";
            this.btnRemoveAll.UseVisualStyleBackColor = true;
            this.btnRemoveAll.Click += new System.EventHandler(this.btnRemoveAll_Click);
            // 
            // btnRefresh
            // 
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRefresh.Location = new System.Drawing.Point(244, 283);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(80, 30);
            this.btnRefresh.TabIndex = 3;
            this.btnRefresh.Text = "Refresh";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            // 
            // chkAutoRemove
            // 
            this.chkAutoRemove.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.chkAutoRemove.AutoSize = true;
            this.chkAutoRemove.Location = new System.Drawing.Point(420, 291);
            this.chkAutoRemove.Name = "chkAutoRemove";
            this.chkAutoRemove.Size = new System.Drawing.Size(302, 17);
            this.chkAutoRemove.TabIndex = 4;
            this.chkAutoRemove.Text = "Automatically remove temporary IP aliases on application exit";
            this.chkAutoRemove.UseVisualStyleBackColor = true;
            this.chkAutoRemove.CheckedChanged += new System.EventHandler(this.chkAutoRemove_CheckedChanged);
            // 
            // TemporarySubnetManagerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(734, 321);
            this.Controls.Add(this.chkAutoRemove);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.btnRemoveAll);
            this.Controls.Add(this.btnRemoveSelected);
            this.Controls.Add(this.listView);
            this.MinimumSize = new System.Drawing.Size(750, 360);
            this.Name = "TemporarySubnetManagerForm";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Temporary Subnet Manager";
            this.Load += new System.EventHandler(this.TemporarySubnetManagerForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }
    }
}
