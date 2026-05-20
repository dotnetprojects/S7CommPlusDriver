using S7CommPlusDriver;
using S7CommPlusDriver.ClientApi;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace S7CommPlusGUIBrowser
{
    public partial class Form1 : Form
    {
        private S7CommPlusClient client;

        public Form1()
        {
            InitializeComponent();

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length >= 2)
            {
                tbIpAddress.Text = args[1];
            }
            if (args.Length >= 3)
            {
                tbPassword.Text = args[2];
            }
            if (args.Length >= 4)
            {
                tbUser.Text = args[3];
            }
        }

        private void setStatus(string status)
        {
            lbStatus.Text = status;
            lbStatus.Refresh();
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            setStatus("connecting...");
            await CloseClientAsync().ConfigureAwait(true);

            client = new S7CommPlusClient(new S7CommPlusClientOptions
            {
                Address = tbIpAddress.Text,
                Password = tbPassword.Text,
                Username = tbUser.Text
            });

            try
            {
                await client.ConnectAsync().ConfigureAwait(true);
                setStatus("loading...");
                await LoadBrowseTreeAsync().ConfigureAwait(true);
                setStatus("connected");
            }
            catch (S7CommPlusException ex)
            {
                setStatus("error: " + ex.Message);
                await CloseClientAsync().ConfigureAwait(true);
            }
        }

        private async void btnDisconnect_Click(object sender, EventArgs e)
        {
            setStatus("disconnecting...");
            await CloseClientAsync().ConfigureAwait(true);
            treeView1.Nodes.Clear();
            setStatus("disconnected");
        }

        private async void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            await CloseClientAsync().ConfigureAwait(true);
        }

        private async Task LoadBrowseTreeAsync()
        {
            treeView1.BeginUpdate();
            try
            {
                treeView1.Nodes.Clear();

                var variablesNode = treeView1.Nodes.Add("Variables");
                variablesNode.ImageKey = "Tag";
                variablesNode.SelectedImageKey = variablesNode.ImageKey;

                var blocksNode = treeView1.Nodes.Add("Blocks");
                blocksNode.ImageKey = "Datablock";
                blocksNode.SelectedImageKey = blocksNode.ImageKey;

                await LoadBrowseTreeCoreAsync(variablesNode, blocksNode).ConfigureAwait(true);
            }
            finally
            {
                treeView1.EndUpdate();
            }
        }

        private async Task LoadBrowseTreeCoreAsync(TreeNode variablesNode, TreeNode blocksNode)
        {
            foreach (var variable in await client.BrowseAsync().ConfigureAwait(true))
            {
                var node = variablesNode.Nodes.Add(variable.Name);
                node.Tag = variable;
                node.ImageKey = "Tag";
                node.SelectedImageKey = node.ImageKey;
            }

            foreach (var root in await client.BrowseBlockStructureAsync().ConfigureAwait(true))
            {
                blocksNode.Nodes.Add(CreateStructureNode(root));
            }

            variablesNode.Expand();
            blocksNode.Expand();
        }

        private static TreeNode CreateStructureNode(S7CommPlusPlcStructureNode structureNode)
        {
            var text = structureNode.Number.HasValue
                ? $"{structureNode.BlockType ?? structureNode.Kind.ToString()} {structureNode.Number}: {structureNode.Name}"
                : structureNode.Name;

            var node = new TreeNode(text)
            {
                Tag = structureNode,
                ImageKey = structureNode.Kind == S7CommPlusPlcStructureNodeKind.Block ? "Datablock" : "Default"
            };
            node.SelectedImageKey = node.ImageKey;

            foreach (var child in structureNode.Children)
            {
                node.Nodes.Add(CreateStructureNode(child));
            }

            return node;
        }

        private async Task CloseClientAsync()
        {
            if (client == null)
            {
                return;
            }

            var oldClient = client;
            client = null;
            await oldClient.DisposeAsync().ConfigureAwait(true);
        }

        private async Task ReadTagBySymbolAsync()
        {
            tbValue.Text = string.Empty;
            tbSymbolicAddress.Text = string.Empty;

            if (client == null || string.IsNullOrWhiteSpace(tbSymbol.Text))
            {
                return;
            }

            setStatus("loading...");
            var tag = await client.GetTagBySymbolAsync(tbSymbol.Text).ConfigureAwait(true);
            tbSymbolicAddress.Text = tag.Address.GetAccessString();

            await client.ReadAsync(new[] { tag }).ConfigureAwait(true);
            tbValue.Text = tag.ToString();
            setStatus("connected");
        }

        private async void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is not VarInfo variable)
            {
                return;
            }

            tbSymbol.Text = variable.Name;
            try
            {
                await ReadTagBySymbolAsync().ConfigureAwait(true);
            }
            catch (S7CommPlusException ex)
            {
                setStatus("error: " + ex.Message);
            }
        }

        private void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
        }

        private async void btnRead_Click(object sender, EventArgs e)
        {
            try
            {
                await ReadTagBySymbolAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("ERROR: " + ex.Message);
            }
        }
    }
}
