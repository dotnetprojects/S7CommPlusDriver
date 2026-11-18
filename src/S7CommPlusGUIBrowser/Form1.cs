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
        private S7CommPlusSymbolBrowser symbolBrowser;

        public Form1()
        {
            InitializeComponent();
            cbBrowseMode.SelectedIndex = 0;

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

        /// <summary>
        /// The flat mode returns the complete symbol catalog of the PLC in one call; the per-level
        /// mode expands one data-block interface level per user action through S7CommPlusSymbolBrowser.
        /// </summary>
        private bool UsePerLevelBrowse => cbBrowseMode.SelectedIndex == 1;

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
                symbolBrowser = new S7CommPlusSymbolBrowser(client);
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

        private async void cbBrowseMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (client == null || !client.IsConnected)
            {
                return;
            }

            setStatus("loading...");
            try
            {
                await LoadBrowseTreeAsync().ConfigureAwait(true);
                setStatus("connected");
            }
            catch (Exception ex)
            {
                setStatus("error: " + ex.Message);
            }
        }

        private async Task LoadBrowseTreeAsync()
        {
            treeView1.BeginUpdate();
            try
            {
                treeView1.Nodes.Clear();
                symbolBrowser?.ClearCache();

                if (UsePerLevelBrowse)
                {
                    await LoadPerLevelBrowseTreeAsync().ConfigureAwait(true);
                }
                else
                {
                    await LoadFlatBrowseTreeAsync().ConfigureAwait(true);
                }
            }
            finally
            {
                treeView1.EndUpdate();
            }
        }

        private async Task LoadFlatBrowseTreeAsync()
        {
            var variablesNode = treeView1.Nodes.Add("Variables");
            variablesNode.ImageKey = "Tag";
            variablesNode.SelectedImageKey = variablesNode.ImageKey;

            var blocksNode = treeView1.Nodes.Add("Blocks");
            blocksNode.ImageKey = "Datablock";
            blocksNode.SelectedImageKey = blocksNode.ImageKey;

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

        private async Task LoadPerLevelBrowseTreeAsync()
        {
            var variablesNode = treeView1.Nodes.Add("Data blocks");
            variablesNode.ImageKey = "Datablock";
            variablesNode.SelectedImageKey = variablesNode.ImageKey;

            await LoadBrowseChildrenAsync(variablesNode, null).ConfigureAwait(true);

            var blocksNode = treeView1.Nodes.Add("Blocks");
            blocksNode.ImageKey = "Datablock";
            blocksNode.SelectedImageKey = blocksNode.ImageKey;

            foreach (var root in await client.BrowseBlockStructureAsync().ConfigureAwait(true))
            {
                blocksNode.Nodes.Add(CreateStructureNode(root));
            }

            variablesNode.Expand();
            blocksNode.Expand();
        }

        /// <summary>
        /// Loads the direct children of one browse node. A placeholder child keeps nodes with children
        /// expandable until the user actually requests the next level.
        /// </summary>
        private async Task LoadBrowseChildrenAsync(TreeNode parent, string symbolPath)
        {
            var children = await symbolBrowser.BrowseNodeAsync(symbolPath).ConfigureAwait(true);

            parent.Nodes.Clear();
            foreach (var child in children)
            {
                var node = new TreeNode(DescribeBrowseNode(child))
                {
                    Tag = child,
                    ImageKey = child.HasChildren ? "Structure" : "Tag"
                };
                node.SelectedImageKey = node.ImageKey;

                if (child.HasChildren)
                {
                    node.Nodes.Add(new TreeNode());
                }

                parent.Nodes.Add(node);
            }
        }

        private static string DescribeBrowseNode(S7CommPlusBrowseNode browseNode)
        {
            if (browseNode.HasChildren || string.IsNullOrEmpty(browseNode.NativeTypeName))
            {
                return browseNode.Name;
            }

            return browseNode.Name + " : " + browseNode.NativeTypeName;
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
            symbolBrowser = null;
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
            if (e.Node?.Tag is S7CommPlusBrowseNode browseNode)
            {
                tbSymbol.Text = browseNode.SymbolPath;

                // Structs, arrays, and system types without an access sequence can only be read
                // through their leaf children.
                if (browseNode.HasChildren || browseNode.AccessSequence == null)
                {
                    return;
                }

                try
                {
                    await ReadTagBySymbolAsync().ConfigureAwait(true);
                }
                catch (S7CommPlusException ex)
                {
                    setStatus("error: " + ex.Message);
                }

                return;
            }

            if (e.Node?.Tag is not VarInfo variable)
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

        private async void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (!UsePerLevelBrowse || e.Node == null)
            {
                return;
            }

            if (e.Node.Tag is not S7CommPlusBrowseNode browseNode)
            {
                return;
            }

            // A single untyped placeholder child marks a node whose level has not been loaded yet.
            if (e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Tag != null)
            {
                return;
            }

            setStatus("loading...");
            try
            {
                await LoadBrowseChildrenAsync(e.Node, browseNode.SymbolPath).ConfigureAwait(true);
                setStatus("connected");
            }
            catch (Exception ex)
            {
                setStatus("error: " + ex.Message);
            }
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

