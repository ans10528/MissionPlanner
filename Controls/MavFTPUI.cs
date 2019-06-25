using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;
using MissionPlanner.ArduPilot.Mavlink;

namespace MissionPlanner.Controls
{
    public partial class MavFTPUI : UserControl
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private MAVLinkInterface _mav;
        private MAVFtp _mavftp;

        public MavFTPUI(MAVLinkInterface mav)
        {
            _mav = mav;
            _mavftp = new MAVFtp(_mav, (byte) _mav.sysidcurrent, (byte) mav.compidcurrent);
            _mavftp.Progress += (percent) =>
            {
                this.BeginInvokeIfRequired(() => { toolStripProgressBar1.Value = percent; statusStrip1.Refresh(); });
            };
            InitializeComponent();
            PopulateTreeView();
        }

        private void PopulateTreeView()
        {
            toolStripStatusLabel1.Text = "Updating Folders";

            treeView1.Nodes.Clear();

            TreeNode rootNode;

            DirectoryInfo info = new DirectoryInfo(@"/", _mavftp);
            if (info.Exists)
            {
                rootNode = new TreeNode(info.Name);
                rootNode.Tag = info;
                GetDirectories(info.GetDirectories(), rootNode);
                treeView1.Nodes.Add(rootNode);
            }
            toolStripStatusLabel1.Text = "Ready";
        }

        private void GetDirectories(DirectoryInfo[] subDirs,
            TreeNode nodeToAddTo)
        {
            TreeNode aNode;
            DirectoryInfo[] subSubDirs;
            foreach (DirectoryInfo subDir in subDirs)
            {
                aNode = new TreeNode(subDir.Name, 0, 0);
                aNode.Tag = subDir;
                aNode.ImageKey = "folder";
                subSubDirs = subDir.GetDirectories();
                if (subSubDirs.Length != 0)
                {
                    GetDirectories(subSubDirs, aNode);
                }

                nodeToAddTo.Nodes.Add(aNode);
            }
        }

        private void TreeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            TreeNode newSelected = e.Node;
            listView1.Items.Clear();
            DirectoryInfo nodeDirInfo = (DirectoryInfo) newSelected.Tag;
            ListViewItem.ListViewSubItem[] subItems;
            ListViewItem item = null;

            foreach (DirectoryInfo dir in nodeDirInfo.GetDirectories())
            {
                item = new ListViewItem(dir.Name, 0);
                subItems = new ListViewItem.ListViewSubItem[]
                {
                    new ListViewItem.ListViewSubItem(item, "Directory"),
                    new ListViewItem.ListViewSubItem(item, "".ToString())
                };
                item.SubItems.AddRange(subItems);
                listView1.Items.Add(item);
            }

            foreach (var file in nodeDirInfo.GetFiles())
            {
                item = new ListViewItem(file.Name, 1);
                subItems = new ListViewItem.ListViewSubItem[]
                {
                    new ListViewItem.ListViewSubItem(item, "File"),
                    new ListViewItem.ListViewSubItem(item,file.Size.ToString())
                };

                item.SubItems.AddRange(subItems);
                listView1.Items.Add(item);
            }

            listView1.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);

        }

        public class DirectoryInfo: FileSystemInfo
        {
            private readonly MAVFtp _mavftp;

            public DirectoryInfo(string dir, MAVFtp mavftp)
            {
                _mavftp = mavftp;
                FullPath = dir;
            }

            public override string Name
            {
                get { return Path.GetFileName(FullPath); }
            }

            public override bool Exists => true;

            public override void Delete()
            {
                _mavftp.kCmdRemoveDirectory(FullPath);
            }

            public DirectoryInfo[] GetDirectories()
            {
                return _mavftp.kCmdListDirectory(FullPath).Where(a => a.isDirectory)
                    .Select(a => new DirectoryInfo(a.FullName, _mavftp)).ToArray();
            }

            public IEnumerable<MAVFtp.FtpFileInfo> GetFiles()
            {
                return _mavftp.kCmdListDirectory(FullPath).Where(a => !a.isDirectory);
            }
        }

        private void ListView1_DragDrop(object sender, DragEventArgs e)
        {

        }

        private void ListView1_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            listView1.Sorting = ( SortOrder)(((int) (listView1.Sorting + 1))% 3);
        }

        private async void DownloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem listView1SelectedItem in listView1.SelectedItems)
            {
                toolStripStatusLabel1.Text = "Download " + listView1SelectedItem.Text;
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.FileName = listView1SelectedItem.Text;
                sfd.RestoreDirectory = true;
                sfd.OverwritePrompt = true;
                var dr = sfd.ShowDialog();
                if (dr == DialogResult.OK)
                {
                    var path = treeView1.SelectedNode.FullPath + "/" + listView1SelectedItem.Text;
                    await Task.Run(() =>
                    {
                        // watch all traffic
                        var sub = _mav.SubscribeToPacketType(MAVLink.MAVLINK_MSG_ID.FILE_TRANSFER_PROTOCOL, message =>
                        {
                            var msg = (MAVLink.mavlink_file_transfer_protocol_t)message.data;
                            ArduPilot.Mavlink.MAVFtp.FTPPayloadHeader ftphead = msg.payload;
                            log.Debug(ftphead);
                            Console.WriteLine(ftphead);

                            return true;
                        });

                        var ms = _mavftp.GetFile(path);
                        File.WriteAllBytes(sfd.FileName, ms.ToArray());
                    });
                }
                else if (dr == DialogResult.Cancel)
                {
                    return;
                }
            }
        }

        private async void UploadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = true;
            var dr = ofd.ShowDialog();
            if (dr == DialogResult.OK)
            {
                foreach (var ofdFileName in ofd.FileNames)
                {
                    toolStripStatusLabel1.Text = "Upload " + Path.GetFileName(ofdFileName);
                    var fn = treeView1.SelectedNode.FullPath + "/" + Path.GetFileName(ofdFileName);
                    await Task.Run(() =>
                    {
                        var size = 0;
                        _mavftp.kCmdOpenFileWO(fn, ref size);
                        _mavftp.kCmdWriteFile(ofdFileName);
                        _mavftp.kCmdResetSessions();
                    });
                }
            }

            TreeView1_NodeMouseClick(null,
                new TreeNodeMouseClickEventArgs(treeView1.SelectedNode, MouseButtons.Left, 1, 1, 1));
        }

        private void DeleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem listView1SelectedItem in listView1.SelectedItems)
            {
                toolStripStatusLabel1.Text = "Delete " + listView1SelectedItem.Text;
                _mavftp.kCmdRemoveFile(treeView1.SelectedNode.FullPath + "/" + listView1SelectedItem.Text);
            }

            TreeView1_NodeMouseClick(null,
                new TreeNodeMouseClickEventArgs(treeView1.SelectedNode, MouseButtons.Left, 1, 1, 1));
        }

        private void RenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.SelectedItems[0].BeginEdit();
        }

        private void ListView1_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            _mavftp.kCmdRename(treeView1.SelectedNode.FullPath + "/" + listView1.SelectedItems[0].Text,
                treeView1.SelectedNode.FullPath + "/" + e.Label);

            TreeView1_NodeMouseClick(null,
                new TreeNodeMouseClickEventArgs(treeView1.SelectedNode, MouseButtons.Left, 1, 1, 1));
        }

        private void NewFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string folder = "";
            var dr = InputBox.Show("Folder Name", "Enter folder name", ref folder);
            if (dr == DialogResult.OK)
                _mavftp.kCmdCreateDirectory(treeView1.SelectedNode.FullPath + "/" + folder);

            TreeView1_NodeMouseClick(null,
                new TreeNodeMouseClickEventArgs(treeView1.SelectedNode, MouseButtons.Left, 1, 1, 1));

            PopulateTreeView();
        }
    }


}
