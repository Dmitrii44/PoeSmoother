using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ionic.Zip;
using LibGGPK;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace PoeSmoother
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private string ggpkPath = string.Empty;
        private GGPK content;
        private Thread workerThread;

        /// <summary>
        /// Dictionary mapping ggpk file paths to FileRecords for easy lookup
        /// EG: "Scripts\foobar.mel" -> FileRecord{Foobar.mel}
        /// </summary>
        private Dictionary<string, FileRecord> RecordsByPath;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void OutputLine(string msg)
        {
            Output(msg + Environment.NewLine);
        }

        private void Output(string msg)
        {
            textBoxOutput.Dispatcher.BeginInvoke(new Action(() =>
            {
                textBoxOutput.Text += msg;
            }), null);
        }

        private void UpdateTitle(string newTitle)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Title = newTitle;
            }), null);
        }

        /// <summary>
        /// Reloads the entire content.ggpk, rebuilds the tree
        /// </summary>
        private void ReloadGGPK()
        {
            treeView1.Items.Clear();
            ResetViewer();
            textBoxOutput.Visibility = Visibility.Visible;
            textBoxOutput.Text = string.Empty;
            content = null;

            workerThread = new Thread(() =>
            {
                content = new GGPK();
                try
                {
                    content.Read(ggpkPath, Output);
                }
                catch (Exception ex)
                {
                    Output(string.Format(Settings.Strings["ReloadGGPK_Failed"], ex.Message));
                    return;
                }

                if (content.IsReadOnly)
                {
                    Output(Settings.Strings["ReloadGGPK_ReadOnly"] + Environment.NewLine);
                    UpdateTitle(Settings.Strings["MainWindow_Title_Readonly"]);
                }

                OutputLine(Settings.Strings["ReloadGGPK_Traversing_Tree"]);

                // Collect all FileRecordPath -> FileRecord pairs for easier replacing
                RecordsByPath = new Dictionary<string, FileRecord>(content.RecordOffsets.Count);
                DirectoryTreeNode.TraverseTreePostorder(content.DirectoryRoot, null, n => RecordsByPath.Add(n.GetDirectoryPath() + n.Name, n));

                treeView1.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        AddDirectoryTreeToControl(content.DirectoryRoot, null);
                    }
                    catch (Exception ex)
                    {
                        Output(string.Format(Settings.Strings["Error_Read_Directory_Tree"], ex.Message));
                        return;
                    }

                    workerThread = null;
                }), null);

                OutputLine(Settings.Strings["ReloadGGPK_Successful"]);
            });

            workerThread.Start();
        }

        /// <summary>
        /// Recursivly adds the specified GGPK DirectoryTree to the TreeListView
        /// </summary>
        /// <param name="directoryTreeNode">Node to add to tree</param>
        /// <param name="parentControl">TreeViewItem to add children to</param>
        private void AddDirectoryTreeToControl(DirectoryTreeNode directoryTreeNode, TreeViewItem parentControl)
        {
            TreeViewItem rootItem = new TreeViewItem { Header = directoryTreeNode };
            if ((directoryTreeNode.ToString() == "ROOT") || (directoryTreeNode.ToString() == "")) rootItem.IsExpanded = true;

            if (parentControl == null)
            {
                treeView1.Items.Add(rootItem);
            }
            else
            {
                parentControl.Items.Add(rootItem);
            }

            directoryTreeNode.Children.Sort();
            foreach (var item in directoryTreeNode.Children)
            {
                AddDirectoryTreeToControl(item, rootItem);
            }

            directoryTreeNode.Files.Sort();
            foreach (var item in directoryTreeNode.Files)
            {
                rootItem.Items.Add(item);
            }
        }

        /// <summary>
        /// Resets all of the file viewers
        /// </summary>
        private void ResetViewer()
        {
            textBoxOutput.Visibility = Visibility.Hidden;
            richTextOutput.Visibility = Visibility.Hidden;
            textBoxOutput.Clear();
            richTextOutput.Document.Blocks.Clear();
        }

        /// <summary>
        /// Updates the FileViewers to display the currently selected item in the TreeView
        /// </summary>
        private void UpdateDisplayPanel()
        {
            ResetViewer();

            if (treeView1.SelectedItem == null)
            {
                return;
            }

            var item = treeView1.SelectedItem as TreeViewItem;
            if (item?.Header is DirectoryTreeNode)
            {
                DirectoryTreeNode selectedDirectory = (DirectoryTreeNode)item.Header;
                if (selectedDirectory.Record == null)
                    return;
            }

            FileRecord selectedRecord = treeView1.SelectedItem as FileRecord;
            if (selectedRecord == null)
                return;
            try
            {
                switch (selectedRecord.FileFormat)
                {
                    case FileRecord.DataFormat.Ascii:
                        DisplayAscii(selectedRecord);
                        break;

                    case FileRecord.DataFormat.Unicode:
                        DisplayUnicode(selectedRecord);
                        break;

                    case FileRecord.DataFormat.RichText:
                        DisplayRichText(selectedRecord);
                        break;
                }
            }
            catch (Exception ex)
            {
                ResetViewer();
                textBoxOutput.Visibility = Visibility.Visible;

                StringBuilder sb = new StringBuilder();
                while (ex != null)
                {
                    sb.AppendLine(ex.Message);
                    ex = ex.InnerException;
                }

                textBoxOutput.Text = string.Format(Settings.Strings["UpdateDisplayPanel_Failed"], sb);
            }
        }

        /// <summary>
        /// Displays the contents of a FileRecord in the RichTextBox
        /// </summary>
        /// <param name="selectedRecord">FileRecord to display</param>
        private void DisplayRichText(FileRecord selectedRecord)
        {
            byte[] buffer = selectedRecord.ReadData(ggpkPath);
            richTextOutput.Visibility = Visibility.Visible;

            using (MemoryStream ms = new MemoryStream(buffer))
            {
                richTextOutput.Selection.Load(ms, DataFormats.Rtf);
            }
        }

        /// <summary>
        /// Displays the contents of a FileRecord in the TextBox as Unicode text
        /// </summary>
        /// <param name="selectedRecord">FileRecord to display</param>
        private void DisplayUnicode(FileRecord selectedRecord)
        {
            byte[] buffer = selectedRecord.ReadData(ggpkPath);
            textBoxOutput.Visibility = Visibility.Visible;

            textBoxOutput.Text = Encoding.Unicode.GetString(buffer);
        }

        /// <summary>
        /// Displays the contents of a FileRecord in the TextBox as Ascii text
        /// </summary>
        /// <param name="selectedRecord">FileRecord to display</param>
        private void DisplayAscii(FileRecord selectedRecord)
        {
            byte[] buffer = selectedRecord.ReadData(ggpkPath);
            textBoxOutput.Visibility = Visibility.Visible;

            textBoxOutput.Text = Encoding.ASCII.GetString(buffer);
        }

        /// <summary>
        /// Exports the specified FileRecord to disk
        /// </summary>
        /// <param name="selectedRecord">FileRecord to export</param>
        private void ExportFileRecord(FileRecord selectedRecord)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog { FileName = selectedRecord.Name };
                if (saveFileDialog.ShowDialog() == true)
                {
                    selectedRecord.ExtractFile(ggpkPath, saveFileDialog.FileName);
                    MessageBox.Show(string.Format(Settings.Strings["ExportSelectedItem_Successful"], selectedRecord.DataLength), Settings.Strings["ExportAllItemsInDirectory_Successful_Caption"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ExportSelectedItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Exports entire DirectoryTreeNode to disk, preserving directory structure
        /// </summary>
        /// <param name="selectedDirectoryNode">Node to export to disk</param>
        private void ExportAllItemsInDirectory(DirectoryTreeNode selectedDirectoryNode)
        {
            List<FileRecord> recordsToExport = new List<FileRecord>();

            Action<FileRecord> fileAction = recordsToExport.Add;

            DirectoryTreeNode.TraverseTreePreorder(selectedDirectoryNode, null, fileAction);

            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = Settings.Strings["ExportAllItemsInDirectory_Default_FileName"]
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    string exportDirectory = Path.GetDirectoryName(saveFileDialog.FileName) + Path.DirectorySeparatorChar;
                    foreach (var item in recordsToExport)
                    {
                        item.ExtractFileWithDirectoryStructure(ggpkPath, exportDirectory);
                    }
                    MessageBox.Show(string.Format(Settings.Strings["ExportAllItemsInDirectory_Successful"], recordsToExport.Count), Settings.Strings["ExportAllItemsInDirectory_Successful_Caption"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ExportAllItemsInDirectory_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Replaces selected file with file user selects via MessageBox
        /// </summary>
        /// <param name="recordToReplace"></param>
        private void ReplaceItem(FileRecord recordToReplace)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    FileName = "",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (openFileDialog.ShowDialog() != true) return;
                recordToReplace.ReplaceContents(ggpkPath, openFileDialog.FileName, content.FreeRoot);
                MessageBox.Show(string.Format(
                    Settings.Strings["ReplaceItem_Successful"], recordToReplace.Name, recordToReplace.RecordBegin.ToString("X")),
                    Settings.Strings["ReplaceItem_Successful_Caption"],
                    MessageBoxButton.OK, MessageBoxImage.Information);

                UpdateDisplayPanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Extracts specified archive and replaces files in GGPK with extracted files. Files in
        /// archive must have same directory structure as in GGPK.
        /// </summary>
        /// <param name="archivePath">Path to archive containing</param>
        private void HandleDropArchive(string archivePath)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropArchive_Info"], archivePath));

            using (ZipFile zipFile = new ZipFile(archivePath))
            {
                //var fileNames = zipFile.EntryFileNames;

                // Archive Version Check: Read version.txt and check with patch_notes.rtf's Hash
                foreach (var item in zipFile.Entries.Where(item => item.FileName.Equals("version.txt")))
                {
                    using (var reader = item.OpenReader())
                    {
                        byte[] versionData = new byte[item.UncompressedSize];
                        reader.Read(versionData, 0, versionData.Length);
                        string versionStr = Encoding.UTF8.GetString(versionData, 0, versionData.Length);
                        if (RecordsByPath.ContainsKey("patch_notes.rtf"))
                        {
                            string Hash = BitConverter.ToString(RecordsByPath["patch_notes.rtf"].Hash);
                            if (!versionStr.Substring(0, Hash.Length).Equals(Hash))
                            {
                                OutputLine(Settings.Strings["MainWindow_VersionCheck_Failed"]);
                                return;
                            }
                        }
                    }
                    break;
                }

                foreach (var item in zipFile.Entries)
                {
                    if (item.IsDirectory)
                    {
                        continue;
                    }
                    if (item.FileName.Equals("version.txt"))
                    {
                        continue;
                    }

                    string fixedFileName = item.FileName;
                    if (Path.DirectorySeparatorChar != '/')
                    {
                        fixedFileName = fixedFileName.Replace('/', Path.DirectorySeparatorChar);
                    }

                    if (!RecordsByPath.ContainsKey(fixedFileName))
                    {
                        OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Failed"], fixedFileName));
                        continue;
                    }
                    OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Replace"], fixedFileName));

                    using (var reader = item.OpenReader())
                    {
                        byte[] replacementData = new byte[item.UncompressedSize];
                        reader.Read(replacementData, 0, replacementData.Length);

                        RecordsByPath[fixedFileName].ReplaceContents(ggpkPath, replacementData, content.FreeRoot);
                    }
                }
            }
        }

        /// <summary>
        /// Replaces the currently selected TreeViewItem with specified file on disk
        /// </summary>
        /// <param name="fileName">Path of file to replace currently selected item with.</param>
        private void HandleDropFile(string fileName)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            FileRecord record = treeView1.SelectedItem as FileRecord;
            if (record == null)
            {
                OutputLine(Settings.Strings["MainWindow_HandleDropFile_Failed"]);
                return;
            }

            OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropFile_Replace"], record.GetDirectoryPath(), record.Name));

            record.ReplaceContents(ggpkPath, fileName, content.FreeRoot);
        }

        /// <summary>
        /// Specified directory was dropped onto interface, attept to replace GGPK files with same directory
        /// structure with files in directory. Directory must have same directory structure as GGPK file.
        /// EG:
        /// dropping 'Art' directory containing '2DArt' directory containing 'BuffIcons' directory containing 'buffbleed.dds' will replace
        /// \Art\2DArt\BuffIcons\buffbleed.dds with buffbleed.dds from dropped directory
        /// </summary>
        /// <param name="baseDirectory">Directory containing files to replace</param>
        private void HandleDropDirectory(string baseDirectory)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            string[] filesToReplace = Directory.GetFiles(baseDirectory, "*.*", SearchOption.AllDirectories);
            var fileName = Path.GetFileName(baseDirectory);
            if (fileName != null)
            {
                int baseDirectoryNameLength = fileName.Length;

                OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Count"], filesToReplace.Length));
                foreach (var item in filesToReplace)
                {
                    string fixedFileName = item.Remove(0, baseDirectory.Length - baseDirectoryNameLength);
                    if (!RecordsByPath.ContainsKey(fixedFileName))
                    {
                        OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Failed"], fixedFileName));
                        continue;
                    }
                    OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Replace"], fixedFileName));

                    RecordsByPath[fixedFileName].ReplaceContents(ggpkPath, item, content.FreeRoot);
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = Settings.Strings["Load_GGPK_Filter"]
            };
            // Get InstallLocation From RegistryKey
            if ((ofd.InitialDirectory == null) || (ofd.InitialDirectory == string.Empty))
            {
                RegistryKey start = Registry.CurrentUser;
                RegistryKey programName = start.OpenSubKey(@"Software\GrindingGearGames\Path of Exile");
                if (programName != null)
                {
                    string pathString = (string)programName.GetValue("InstallLocation");
                    if (pathString != string.Empty && File.Exists(pathString + @"\Content.ggpk"))
                    {
                        ofd.InitialDirectory = pathString;
                    }
                }
            }
            // Get Garena PoE
            if ((ofd.InitialDirectory == null) || (ofd.InitialDirectory == string.Empty))
            {
                RegistryKey start = Registry.LocalMachine;
                RegistryKey programName = start.OpenSubKey(@"SOFTWARE\Wow6432Node\Garena\PoE");
                if (programName != null)
                {
                    string pathString = (string)programName.GetValue("Path");
                    if (pathString != string.Empty && File.Exists(pathString + @"\Content.ggpk"))
                    {
                        ofd.InitialDirectory = pathString;
                    }
                }
            }
            if (ofd.ShowDialog() == true)
            {
                if (!File.Exists(ofd.FileName))
                {
                    Close();
                    return;
                }
                ggpkPath = ofd.FileName;
                ReloadGGPK();
            }
            else
            {
                Close();
                return;
            }

            menuItemExport.Header = Settings.Strings["MainWindow_Menu_Export"];
            menuItemReplace.Header = Settings.Strings["MainWindow_Menu_Replace"];
        }

        private void treeView1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            UpdateDisplayPanel();

            menuItemReplace.IsEnabled = treeView1.SelectedItem is FileRecord;

            if (treeView1.SelectedItem is FileRecord)
            {
                // Exporting file
                menuItemExport.IsEnabled = true;
            }
            else if ((treeView1.SelectedItem as TreeViewItem)?.Header is DirectoryTreeNode)
            {
                // Exporting entire directory
                menuItemExport.IsEnabled = true;
            }
            else
            {
                menuItemExport.IsEnabled = false;
            }
        }

        private void menuItemExport_Click(object sender, RoutedEventArgs e)
        {
            var item = treeView1.SelectedItem as TreeViewItem;
            if (item != null)
            {
                TreeViewItem selectedTreeViewItem = item;
                DirectoryTreeNode selectedDirectoryNode = selectedTreeViewItem.Header as DirectoryTreeNode;
                if (selectedDirectoryNode != null)
                {
                    ExportAllItemsInDirectory(selectedDirectoryNode);
                }
            }
            else if (treeView1.SelectedItem is FileRecord)
            {
                ExportFileRecord((FileRecord)treeView1.SelectedItem);
            }
        }

        private void menuItemReplace_Click(object sender, RoutedEventArgs e)
        {
            FileRecord recordToReplace = treeView1.SelectedItem as FileRecord;
            if (recordToReplace == null)
                return;

            ReplaceItem(recordToReplace);
        }

        private void RainParticles(object sender, RoutedEventArgs e) { RainParticles();}
        private void CustomEffects(object sender, RoutedEventArgs e) { CustomEffects(); }
        private void CustomSkills(object sender, RoutedEventArgs e) { CustomSkills(); }
        private void MonsterSounds(object sender, RoutedEventArgs e) { MonsterSounds(); }
        private void CharSounds(object sender, RoutedEventArgs e) { CharSounds(); }
        private void ChargeSounds(object sender, RoutedEventArgs e) { ChargeSounds(); }
        private void PortalSounds(object sender, RoutedEventArgs e) { PortalSounds(); }
        private void HeraldOfIce(object sender, RoutedEventArgs e) { HeraldOfIce(); }
        private void DischargeImproved(object sender, RoutedEventArgs e) { DischargeImproved(); }
        private void DischargeDisabled(object sender, RoutedEventArgs e) { DischargeDisabled(); }
        private void GroundEffects(object sender, RoutedEventArgs e) { GroundEffects(); }
        private void CorruptedArea(object sender, RoutedEventArgs e) { CorruptedArea(); }
        private void ExtraGore(object sender, RoutedEventArgs e) { ExtraGore(); }
        private void CustomSounds(object sender, RoutedEventArgs e) { CustomSounds(); }
        private void DeleteDeadBodies(object sender, RoutedEventArgs e) { DeleteDeadBodies(); }
        private void RemovePets(object sender, RoutedEventArgs e) { RemovePets(); }
        private void GlobeGirls(object sender, RoutedEventArgs e) { GlobeGirls(); }
        private void ZeroParticles(object sender, RoutedEventArgs e) { ZeroParticles(); }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            rainParticles.IsChecked = false;
            groundEffects.IsChecked = false;
            customEffects.IsChecked = false;
            customSkills.IsChecked = false;
            extraGore.IsChecked = false;
            corruptedArea.IsChecked = false;
            monsterSounds.IsChecked = false;
            portalSounds.IsChecked = false;
            charSounds.IsChecked = false;
            chargeSounds.IsChecked = false;
            heraldOfIce.IsChecked = false;
            dischargeDisabled.IsChecked = false;
            dischargeImproved.IsChecked = false;
            customSounds.IsChecked = false;
            deleteDeadBodies.IsChecked = false;
            removePets.IsChecked = false;
            globeGirls.IsChecked = false;
            zeroParticles.IsChecked = false;
        }

        private void CustomEffects()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string DisableCustomEffects = "config/customEffects/enableCustom/Metadata";
            string[] replace_cgs = Directory.GetFiles(DisableCustomEffects, "*.*", SearchOption.AllDirectories);
            var replace_cgs_path = Path.GetFileName(DisableCustomEffects);
            int replace_cgs_dir = replace_cgs_path.Length;

            const string RestoreDefaultEffects = "config/customEffects/restoreDefault/Metadata";
            string[] restore_cgs = Directory.GetFiles(RestoreDefaultEffects, "*.*", SearchOption.AllDirectories);
            var restore_cgs_path = Path.GetFileName(RestoreDefaultEffects);
            int restore_cgs_dir = restore_cgs_path.Length;

            try
            {
                switch (customEffects.IsChecked)
                {
                    case true:
                        {
                            foreach (var item in replace_cgs)
                            {
                                string fileNames = item.Remove(0, DisableCustomEffects.Length - replace_cgs_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            foreach (var item in restore_cgs)
                            {
                                string fileNames = item.Remove(0, RestoreDefaultEffects.Length - restore_cgs_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CustomSounds()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string EnableCustomSounds = "config/customSounds/enableCustom/Audio";
            string[] replace_cgs = Directory.GetFiles(EnableCustomSounds, "*.*", SearchOption.AllDirectories);
            var replace_cgs_path = Path.GetFileName(EnableCustomSounds);
            int replace_cgs_dir = replace_cgs_path.Length;

            const string RestoreDefaultSounds = "config/customSounds/restoreDefault/Audio";
            string[] restore_cgs = Directory.GetFiles(RestoreDefaultSounds, "*.*", SearchOption.AllDirectories);
            var restore_cgs_path = Path.GetFileName(RestoreDefaultSounds);
            int restore_cgs_dir = restore_cgs_path.Length;

            try
            {
                switch (customEffects.IsChecked)
                {
                    case true:
                        {
                            foreach (var item in replace_cgs)
                            {
                                string fileNames = item.Remove(0, EnableCustomSounds.Length - replace_cgs_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            foreach (var item in restore_cgs)
                            {
                                string fileNames = item.Remove(0, RestoreDefaultSounds.Length - restore_cgs_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CustomSkills()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string EnableCustomSkills = "config/customSkills/enableCustom/Metadata";
            string[] replace_cgs = Directory.GetFiles(EnableCustomSkills, "*.*", SearchOption.AllDirectories);
            var replace_cgs_path = Path.GetFileName(EnableCustomSkills);
            int replace_cgs_dir = replace_cgs_path.Length;

            const string RestoreDefaultSkills = "config/customSkills/restoreDefault/Metadata";
            string[] restore_cgs = Directory.GetFiles(RestoreDefaultSkills, "*.*", SearchOption.AllDirectories);
            var restore_cgs_path = Path.GetFileName(RestoreDefaultSkills);
            int restore_cgs_dir = restore_cgs_path.Length;

            try
            {
                switch (customEffects.IsChecked)
                {
                    case true:
                        {
                            foreach (var item in replace_cgs)
                            {
                                string fileNames = item.Remove(0, EnableCustomSkills.Length - replace_cgs_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            foreach (var item in restore_cgs)
                            {
                                string fileNames = item.Remove(0, RestoreDefaultSkills.Length - restore_cgs_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ZeroParticles()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string replaceParticles = "config/replaceParticles/Metadata";
            string[] replaceFiles = Directory.GetFiles(replaceParticles, "*.*", SearchOption.AllDirectories);
            var replace = Path.GetFileName(replaceParticles);
            int replaceDirectoryLength = replace.Length;

            const string restoreParticles = "config/restoreParticles/Metadata";
            string[] restoreFiles = Directory.GetFiles(restoreParticles, "*.*", SearchOption.AllDirectories);
            var restore = Path.GetFileName(restoreParticles);
            int restoreDirectoryLength = restore.Length;
            
            try
            {
                switch (zeroParticles.IsChecked)
                {
                    case true:
                        {
                            foreach (var item in replaceFiles)
                            {
                                string fileNames = item.Remove(0, replaceParticles.Length - replaceDirectoryLength);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            foreach (var item in restoreFiles)
                            {
                                string fileNames = item.Remove(0, restoreParticles.Length - restoreDirectoryLength);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GlobeGirls()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (globeGirls.IsChecked)
                {
                    case true:
                        {
                            const string enable_GlobeGirls = "config/uiSettings/globeGirls.dds";
                            const string GlobeGirls = "Art\\Textures\\Interface\\2D\\2DArt_UIImages_InGame_1.dds";
                            RecordsByPath[GlobeGirls].ReplaceContents(ggpkPath, enable_GlobeGirls, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string disable_GlobeGirls = "config/uiSettings/2DArt_UIImages_InGame_1.dds";
                            const string GlobeGirls = "Art\\Textures\\Interface\\2D\\2DArt_UIImages_InGame_1.dds";
                            RecordsByPath[GlobeGirls].ReplaceContents(ggpkPath, disable_GlobeGirls, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemovePets()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (removePets.IsChecked)
                {
                    case true:
                        {
                            const string disable_Pet = "config/removePets/disable_Pet.otc";
                            const string disable_RoamingCritterBase = "config/removePets/disable_RoamingCritterBase.otc";

                            const string Pet = "Metadata\\Pet\\Pet.otc";
                            const string RoamingCritterBase = "Metadata\\Critters\\RoamingCritterBase.otc";

                            RecordsByPath[Pet].ReplaceContents(ggpkPath, disable_Pet, content.FreeRoot);
                            RecordsByPath[RoamingCritterBase].ReplaceContents(ggpkPath, disable_RoamingCritterBase, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_Pet = "config/removePets/enable_Pet.otc";
                            const string enable_RoamingCritterBase = "config/removePets/enable_RoamingCritterBase.otc";

                            const string Pet = "Metadata\\Pet\\Pet.otc";
                            const string RoamingCritterBase = "Metadata\\Critters\\RoamingCritterBase.otc";

                            RecordsByPath[Pet].ReplaceContents(ggpkPath, enable_Pet, content.FreeRoot);
                            RecordsByPath[RoamingCritterBase].ReplaceContents(ggpkPath, enable_RoamingCritterBase, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteDeadBodies()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (deleteDeadBodies.IsChecked)
                {
                    case true:
                        {
                            const string disable_Monster = "config/deleteDeadBodies/disable_Monster.otc";
                            const string Monster = "Metadata\\Monsters\\Monster.otc";
                            RecordsByPath[Monster].ReplaceContents(ggpkPath, disable_Monster, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_Monster = "config/deleteDeadBodies/enable_Monster.otc";
                            const string Monster = "Metadata\\Monsters\\Monster.otc";
                            RecordsByPath[Monster].ReplaceContents(ggpkPath, enable_Monster, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RainParticles()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (rainParticles.IsChecked)
                {
                    case true:
                        {
                            const string disable_boat_rain = "config/rainParticles/disable_boat_rain.pet";
                            const string disable_rain_1_3_9 = "config/rainParticles/disable_rain_1_3_9.pet";
                            const string disable_rainsparse = "config/rainParticles/disable_rainsparse.pet";

                            const string boat_rain = "Metadata\\Particles\\cargohold\\boat_rain.pet";
                            const string rain_1_3_9 = "Metadata\\Particles\\enviro_effects\\rain\\rain_1_3_9.pet";
                            const string rainsparse = "Metadata\\Particles\\rainsparse.pet";

                            RecordsByPath[rain_1_3_9].ReplaceContents(ggpkPath, disable_rain_1_3_9, content.FreeRoot);
                            RecordsByPath[boat_rain].ReplaceContents(ggpkPath, disable_boat_rain, content.FreeRoot);
                            RecordsByPath[rainsparse].ReplaceContents(ggpkPath, disable_rainsparse, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_boat_rain = "config/rainParticles/enable_boat_rain.pet";
                            const string enable_rain_1_3_9 = "config/rainParticles/enable_rain_1_3_9.pet";
                            const string enable_rainsparse = "config/rainParticles/enable_rainsparse.pet";

                            const string boat_rain = "Metadata\\Particles\\cargohold\\boat_rain.pet";
                            const string rain_1_3_9 = "Metadata\\Particles\\enviro_effects\\rain\\rain_1_3_9.pet";
                            const string rainsparse = "Metadata\\Particles\\rainsparse.pet";

                            RecordsByPath[rain_1_3_9].ReplaceContents(ggpkPath, enable_rain_1_3_9, content.FreeRoot);
                            RecordsByPath[boat_rain].ReplaceContents(ggpkPath, enable_boat_rain, content.FreeRoot);
                            RecordsByPath[rainsparse].ReplaceContents(ggpkPath, enable_rainsparse, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GroundEffects()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (groundEffects.IsChecked)
                {
                    case true:
                        {
                            const string disable_evil_new_cyl = "config/groundEffects/evil/new/disable_cyl.pet";
                            const string disable_evil_cyl = "config/groundEffects/evil/disable_cyl.pet";
                            const string disable_line = "config/groundEffects/evil/disable_line.pet";
                            const string disable_fire_cyl = "config/groundEffects/fire/disable_cyl.pet";
                            const string disable_ice_cyl = "config/groundEffects/ice/disable_cyl.pet";
                            const string disable_lightning_cyl = "config/groundEffects/lightning/disable_lightning_cyl.pet";
                            const string disable_cloud_large = "config/groundEffects/poison/disable_cloud_large.pet";
                            const string disable_cloud_mid = "config/groundEffects/poison/disable_cloud_mid.pet";
                            const string disable_cloud_small = "config/groundEffects/poison/disable_cloud_small.pet";
                            const string disable_tar_cyl = "config/groundEffects/tar/disable_cyl.pet";
                            const string disable_tar_rig = "config/groundEffects/tar/disable_rig.aoc";

                            const string evil_new_cyl = "Metadata\\Particles\\ground_effects\\evil\\new\\cyl.pet";
                            const string evil_cyl = "Metadata\\Particles\\ground_effects\\evil\\cyl.pet";
                            const string line = "Metadata\\Particles\\ground_effects\\evil\\line.pet";
                            const string fire_cyl = "Metadata\\Particles\\ground_effects\\fire\\cyl.pet";
                            const string ice_cyl = "Metadata\\Particles\\ground_effects\\ice\\cyl.pet";
                            const string lightning_cyl = "Metadata\\Particles\\ground_effects\\lightning\\lightning_cyl.pet";
                            const string cloud_large = "Metadata\\Particles\\ground_effects\\poison\\cloud_large.pet";
                            const string cloud_mid = "Metadata\\Particles\\ground_effects\\poison\\cloud_mid.pet";
                            const string cloud_small = "Metadata\\Particles\\ground_effects\\poison\\cloud_small.pet";
                            const string tar_cyl = "Metadata\\Particles\\ground_effects\\tar\\cyl.pet";
                            const string tar_rig = "Metadata\\Effects\\Spells\\ground_effects\\tar\\rig.aoc";

                            RecordsByPath[evil_new_cyl].ReplaceContents(ggpkPath, disable_evil_new_cyl, content.FreeRoot);
                            RecordsByPath[evil_cyl].ReplaceContents(ggpkPath, disable_evil_cyl, content.FreeRoot);
                            RecordsByPath[line].ReplaceContents(ggpkPath, disable_line, content.FreeRoot);
                            RecordsByPath[fire_cyl].ReplaceContents(ggpkPath, disable_fire_cyl, content.FreeRoot);
                            RecordsByPath[ice_cyl].ReplaceContents(ggpkPath, disable_ice_cyl, content.FreeRoot);
                            RecordsByPath[lightning_cyl].ReplaceContents(ggpkPath, disable_lightning_cyl, content.FreeRoot);
                            RecordsByPath[cloud_large].ReplaceContents(ggpkPath, disable_cloud_large, content.FreeRoot);
                            RecordsByPath[cloud_mid].ReplaceContents(ggpkPath, disable_cloud_mid, content.FreeRoot);
                            RecordsByPath[cloud_small].ReplaceContents(ggpkPath, disable_cloud_small, content.FreeRoot);
                            RecordsByPath[tar_cyl].ReplaceContents(ggpkPath, disable_tar_cyl, content.FreeRoot);
                            RecordsByPath[tar_rig].ReplaceContents(ggpkPath, disable_tar_rig, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_evil_new_cyl = "config/groundEffects/evil/new/enable_cyl.pet";
                            const string enable_evil_cyl = "config/groundEffects/evil/enable_cyl.pet";
                            const string enable_line = "config/groundEffects/evil/enable_line.pet";
                            const string enable_fire_cyl = "config/groundEffects/fire/enable_cyl.pet";
                            const string enable_ice_cyl = "config/groundEffects/ice/enable_cyl.pet";
                            const string enable_lightning_cyl = "config/groundEffects/lightning/enable_lightning_cyl.pet";
                            const string enable_cloud_large = "config/groundEffects/poison/enable_cloud_large.pet";
                            const string enable_cloud_mid = "config/groundEffects/poison/enable_cloud_mid.pet";
                            const string enable_cloud_small = "config/groundEffects/poison/enable_cloud_small.pet";
                            const string enable_tar_cyl = "config/groundEffects/tar/enable_cyl.pet";
                            const string enable_tar_rig = "config/groundEffects/tar/enable_rig.aoc";

                            const string evil_new_cyl = "Metadata\\Particles\\ground_effects\\evil\\new\\cyl.pet";
                            const string evil_cyl = "Metadata\\Particles\\ground_effects\\evil\\cyl.pet";
                            const string line = "Metadata\\Particles\\ground_effects\\evil\\line.pet";
                            const string fire_cyl = "Metadata\\Particles\\ground_effects\\fire\\cyl.pet";
                            const string ice_cyl = "Metadata\\Particles\\ground_effects\\ice\\cyl.pet";
                            const string lightning_cyl = "Metadata\\Particles\\ground_effects\\lightning\\lightning_cyl.pet";
                            const string cloud_large = "Metadata\\Particles\\ground_effects\\poison\\cloud_large.pet";
                            const string cloud_mid = "Metadata\\Particles\\ground_effects\\poison\\cloud_mid.pet";
                            const string cloud_small = "Metadata\\Particles\\ground_effects\\poison\\cloud_small.pet";
                            const string tar_cyl = "Metadata\\Particles\\ground_effects\\tar\\cyl.pet";
                            const string tar_rig = "Metadata\\Effects\\Spells\\ground_effects\\tar\\rig.aoc";

                            RecordsByPath[evil_new_cyl].ReplaceContents(ggpkPath, enable_evil_new_cyl, content.FreeRoot);
                            RecordsByPath[evil_cyl].ReplaceContents(ggpkPath, enable_evil_cyl, content.FreeRoot);
                            RecordsByPath[line].ReplaceContents(ggpkPath, enable_line, content.FreeRoot);
                            RecordsByPath[fire_cyl].ReplaceContents(ggpkPath, enable_fire_cyl, content.FreeRoot);
                            RecordsByPath[ice_cyl].ReplaceContents(ggpkPath, enable_ice_cyl, content.FreeRoot);
                            RecordsByPath[lightning_cyl].ReplaceContents(ggpkPath, enable_lightning_cyl, content.FreeRoot);
                            RecordsByPath[cloud_large].ReplaceContents(ggpkPath, enable_cloud_large, content.FreeRoot);
                            RecordsByPath[cloud_mid].ReplaceContents(ggpkPath, enable_cloud_mid, content.FreeRoot);
                            RecordsByPath[cloud_small].ReplaceContents(ggpkPath, enable_cloud_small, content.FreeRoot);
                            RecordsByPath[tar_cyl].ReplaceContents(ggpkPath, enable_tar_cyl, content.FreeRoot);
                            RecordsByPath[tar_rig].ReplaceContents(ggpkPath, enable_tar_rig, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExtraGore()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (extraGore.IsChecked)
                {
                    case true:
                        {
                            const string disable_blood_gibs_01 = "config/extraGore/disable_blood_gibs_01.aoc";
                            const string disable_blood_gibs_02 = "config/extraGore/disable_blood_gibs_02.aoc";
                            const string disable_blood_gibs_03 = "config/extraGore/disable_blood_gibs_03.aoc";
                            const string disable_blood_gibs_04 = "config/extraGore/disable_blood_gibs_04.aoc";
                            const string disable_blood_gibs_05 = "config/extraGore/disable_blood_gibs_05.aoc";
                            const string disable_blood_gibs_06 = "config/extraGore/disable_blood_gibs_06.aoc";
                            const string disable_gibs_yellow_01 = "config/extraGore/disable_gibs_yellow_01.aoc";
                            const string disable_gibs_yellow_02 = "config/extraGore/disable_gibs_yellow_02.aoc";
                            const string disable_gibs_yellow_03 = "config/extraGore/disable_gibs_yellow_03.aoc";
                            const string disable_gibs_yellow_04 = "config/extraGore/disable_gibs_yellow_04.aoc";
                            const string disable_gibs_yellow_05 = "config/extraGore/disable_gibs_yellow_05.aoc";
                            const string disable_gibs_yellow_06 = "config/extraGore/disable_gibs_yellow_06.aoc";
                            const string disable_gibs_zombie_01 = "config/extraGore/disable_gibs_zombie_01.aoc";
                            const string disable_gibs_zombie_02 = "config/extraGore/disable_gibs_zombie_02.aoc";
                            const string disable_gibs_zombie_03 = "config/extraGore/disable_gibs_zombie_03.aoc";
                            const string disable_gibs_zombie_04 = "config/extraGore/disable_gibs_zombie_04.aoc";
                            const string disable_gibs_zombie_05 = "config/extraGore/disable_gibs_zombie_05.aoc";
                            const string disable_gibs_zombie_06 = "config/extraGore/disable_gibs_zombie_06.aoc";
                            const string disable_blood_impact_spider = "config/extraGore/disable_blood_impact_spider.pet";
                            const string disable_blood_impact_zombie = "config/extraGore/disable_blood_impact_zombie.pet";
                            const string disable_blood1_impact_spider = "config/extraGore/disable_blood1_impact_spider.pet";
                            const string disable_blood1_impact_zombie = "config/extraGore/disable_blood1_impact_zombie.pet";
                            const string disable_blood2_impact_spider = "config/extraGore/disable_blood2_impact_spider.pet";
                            const string disable_blood2_impact_zombie = "config/extraGore/disable_blood2_impact_zombie.pet";
                            const string disable_Green = "config/extraGore/disable_Green.pet";
                            const string disable_Orange = "config/extraGore/disable_Orange.pet";
                            const string disable_Red = "config/extraGore/disable_Red.pet";
                            const string disable_critical_strikes_red = "config/extraGore/first_blood/red/disable_critical_strikes_red.pet";
                            const string disable_red_blood = "config/extraGore/first_blood/red/disable_red_blood.pet";
                            const string disable_critical_strikes_green = "config/extraGore/first_blood/spider/disable_critical_strikes_green.pet";
                            const string disable_spider_blood = "config/extraGore/first_blood/spider/disable_spider_blood.pet";
                            const string disable_critical_strikes_zombie = "config/extraGore/first_blood/zombie/disable_critical_strikes_zombie.pet";
                            const string disable_zombie_blood = "config/extraGore/first_blood/zombie/disable_zombie_blood.pet";
                            const string disable_new_critical_strikes_green = "config/extraGore/new/disable_critical_strikes_green.pet";
                            const string disable_new_critical_strikes_orange = "config/extraGore/new/disable_critical_strikes_orange.pet";
                            const string disable_new_critical_strikes_red = "config/extraGore/new/disable_critical_strikes_red.pet";
                            const string disable_new_blood_impact_spider = "config/extraGore/new/green/disable_blood_impact_spider.pet";
                            const string disable_new_blood1_impact_spider = "config/extraGore/new/green/disable_blood1_impact_spider.pet";
                            const string disable_new_blood_impact = "config/extraGore/new/red/disable_blood_impact.pet";
                            const string disable_new_blood1_impact = "config/extraGore/new/red/disable_blood1_impact.pet";
                            const string disable_new_blood_impact_zombie = "config/extraGore/new/zombie/disable_blood_impact_zombie.pet";
                            const string disable_new_blood1_impact_zombie = "config/extraGore/new/zombie/disable_blood1_impact_zombie.pet";

                            const string blood_gibs_01 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_01.aoc";
                            const string blood_gibs_02 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_02.aoc";
                            const string blood_gibs_03 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_03.aoc";
                            const string blood_gibs_04 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_04.aoc";
                            const string blood_gibs_05 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_05.aoc";
                            const string blood_gibs_06 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_06.aoc";
                            const string gibs_yellow_01 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_01.aoc";
                            const string gibs_yellow_02 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_02.aoc";
                            const string gibs_yellow_03 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_03.aoc";
                            const string gibs_yellow_04 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_04.aoc";
                            const string gibs_yellow_05 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_05.aoc";
                            const string gibs_yellow_06 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_06.aoc";
                            const string gibs_zombie_01 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_01.aoc";
                            const string gibs_zombie_02 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_02.aoc";
                            const string gibs_zombie_03 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_03.aoc";
                            const string gibs_zombie_04 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_04.aoc";
                            const string gibs_zombie_05 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_05.aoc";
                            const string gibs_zombie_06 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_06.aoc";
                            const string blood_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_impact_spider.pet";
                            const string blood_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_impact_zombie.pet";
                            const string blood1_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood1_impact_spider.pet";
                            const string blood1_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood1_impact_zombie.pet";
                            const string blood2_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood2_impact_spider.pet";
                            const string blood2_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood2_impact_zombie.pet";
                            const string Green = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\Green.pet";
                            const string Orange = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\Orange.pet";
                            const string Red = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\Red.pet";
                            const string critical_strikes_red = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\red\\critical_strikes_red.pet";
                            const string red_blood = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\red\\red_blood.pet";
                            const string critical_strikes_green = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\spider\\critical_strikes_green.pet";
                            const string spider_blood = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\spider\\spider_blood.pet";
                            const string critical_strikes_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\zombie\\critical_strikes_zombie.pet";
                            const string zombie_blood = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\zombie\\zombie_blood.pet";
                            const string new_critical_strikes_green = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\critical_strikes_green.pet";
                            const string new_critical_strikes_orange = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\critical_strikes_orange.pet";
                            const string new_critical_strikes_red = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\critical_strikes_red.pet";
                            const string new_blood_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\green\\blood_impact_spider.pet";
                            const string new_blood1_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\green\\blood1_impact_spider.pet";
                            const string new_blood_impact = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\red\\blood_impact.pet";
                            const string new_blood1_impact = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\red\\blood1_impact.pet";
                            const string new_blood_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\zombie\\blood_impact_zombie.pet";
                            const string new_blood1_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\zombie\\blood1_impact_zombie.pet";

                            RecordsByPath[blood_gibs_01].ReplaceContents(ggpkPath, disable_blood_gibs_01, content.FreeRoot);
                            RecordsByPath[blood_gibs_02].ReplaceContents(ggpkPath, disable_blood_gibs_02, content.FreeRoot);
                            RecordsByPath[blood_gibs_03].ReplaceContents(ggpkPath, disable_blood_gibs_03, content.FreeRoot);
                            RecordsByPath[blood_gibs_04].ReplaceContents(ggpkPath, disable_blood_gibs_04, content.FreeRoot);
                            RecordsByPath[blood_gibs_05].ReplaceContents(ggpkPath, disable_blood_gibs_05, content.FreeRoot);
                            RecordsByPath[blood_gibs_06].ReplaceContents(ggpkPath, disable_blood_gibs_06, content.FreeRoot);
                            RecordsByPath[gibs_yellow_01].ReplaceContents(ggpkPath, disable_gibs_yellow_01, content.FreeRoot);
                            RecordsByPath[gibs_yellow_02].ReplaceContents(ggpkPath, disable_gibs_yellow_02, content.FreeRoot);
                            RecordsByPath[gibs_yellow_03].ReplaceContents(ggpkPath, disable_gibs_yellow_03, content.FreeRoot);
                            RecordsByPath[gibs_yellow_04].ReplaceContents(ggpkPath, disable_gibs_yellow_04, content.FreeRoot);
                            RecordsByPath[gibs_yellow_05].ReplaceContents(ggpkPath, disable_gibs_yellow_05, content.FreeRoot);
                            RecordsByPath[gibs_yellow_06].ReplaceContents(ggpkPath, disable_gibs_yellow_06, content.FreeRoot);
                            RecordsByPath[gibs_zombie_01].ReplaceContents(ggpkPath, disable_gibs_zombie_01, content.FreeRoot);
                            RecordsByPath[gibs_zombie_02].ReplaceContents(ggpkPath, disable_gibs_zombie_02, content.FreeRoot);
                            RecordsByPath[gibs_zombie_03].ReplaceContents(ggpkPath, disable_gibs_zombie_03, content.FreeRoot);
                            RecordsByPath[gibs_zombie_04].ReplaceContents(ggpkPath, disable_gibs_zombie_04, content.FreeRoot);
                            RecordsByPath[gibs_zombie_05].ReplaceContents(ggpkPath, disable_gibs_zombie_05, content.FreeRoot);
                            RecordsByPath[gibs_zombie_06].ReplaceContents(ggpkPath, disable_gibs_zombie_06, content.FreeRoot);
                            RecordsByPath[blood_impact_spider].ReplaceContents(ggpkPath, disable_blood_impact_spider, content.FreeRoot);
                            RecordsByPath[blood_impact_zombie].ReplaceContents(ggpkPath, disable_blood_impact_zombie, content.FreeRoot);
                            RecordsByPath[blood1_impact_spider].ReplaceContents(ggpkPath, disable_blood1_impact_spider, content.FreeRoot);
                            RecordsByPath[blood1_impact_zombie].ReplaceContents(ggpkPath, disable_blood1_impact_zombie, content.FreeRoot);
                            RecordsByPath[blood2_impact_spider].ReplaceContents(ggpkPath, disable_blood2_impact_spider, content.FreeRoot);
                            RecordsByPath[blood2_impact_zombie].ReplaceContents(ggpkPath, disable_blood2_impact_zombie, content.FreeRoot);
                            RecordsByPath[Green].ReplaceContents(ggpkPath, disable_Green, content.FreeRoot);
                            RecordsByPath[Orange].ReplaceContents(ggpkPath, disable_Orange, content.FreeRoot);
                            RecordsByPath[Red].ReplaceContents(ggpkPath, disable_Red, content.FreeRoot);
                            RecordsByPath[critical_strikes_red].ReplaceContents(ggpkPath, disable_critical_strikes_red, content.FreeRoot);
                            RecordsByPath[red_blood].ReplaceContents(ggpkPath, disable_red_blood, content.FreeRoot);
                            RecordsByPath[critical_strikes_green].ReplaceContents(ggpkPath, disable_critical_strikes_green, content.FreeRoot);
                            RecordsByPath[spider_blood].ReplaceContents(ggpkPath, disable_spider_blood, content.FreeRoot);
                            RecordsByPath[critical_strikes_zombie].ReplaceContents(ggpkPath, disable_critical_strikes_zombie, content.FreeRoot);
                            RecordsByPath[zombie_blood].ReplaceContents(ggpkPath, disable_zombie_blood, content.FreeRoot);
                            RecordsByPath[new_critical_strikes_green].ReplaceContents(ggpkPath, disable_new_critical_strikes_green, content.FreeRoot);
                            RecordsByPath[new_critical_strikes_orange].ReplaceContents(ggpkPath, disable_new_critical_strikes_orange, content.FreeRoot);
                            RecordsByPath[new_critical_strikes_red].ReplaceContents(ggpkPath, disable_new_critical_strikes_red, content.FreeRoot);
                            RecordsByPath[new_blood_impact_spider].ReplaceContents(ggpkPath, disable_new_blood_impact_spider, content.FreeRoot);
                            RecordsByPath[new_blood1_impact_spider].ReplaceContents(ggpkPath, disable_new_blood1_impact_spider, content.FreeRoot);
                            RecordsByPath[new_blood_impact].ReplaceContents(ggpkPath, disable_new_blood_impact, content.FreeRoot);
                            RecordsByPath[new_blood1_impact].ReplaceContents(ggpkPath, disable_new_blood1_impact, content.FreeRoot);
                            RecordsByPath[new_blood_impact_zombie].ReplaceContents(ggpkPath, disable_new_blood_impact_zombie, content.FreeRoot);
                            RecordsByPath[new_blood1_impact_zombie].ReplaceContents(ggpkPath, disable_new_blood1_impact_zombie, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_blood_gibs_01 = "config/extraGore/enable_blood_gibs_01.aoc";
                            const string enable_blood_gibs_02 = "config/extraGore/enable_blood_gibs_02.aoc";
                            const string enable_blood_gibs_03 = "config/extraGore/enable_blood_gibs_03.aoc";
                            const string enable_blood_gibs_04 = "config/extraGore/enable_blood_gibs_04.aoc";
                            const string enable_blood_gibs_05 = "config/extraGore/enable_blood_gibs_05.aoc";
                            const string enable_blood_gibs_06 = "config/extraGore/enable_blood_gibs_06.aoc";
                            const string enable_gibs_yellow_01 = "config/extraGore/enable_gibs_yellow_01.aoc";
                            const string enable_gibs_yellow_02 = "config/extraGore/enable_gibs_yellow_02.aoc";
                            const string enable_gibs_yellow_03 = "config/extraGore/enable_gibs_yellow_03.aoc";
                            const string enable_gibs_yellow_04 = "config/extraGore/enable_gibs_yellow_04.aoc";
                            const string enable_gibs_yellow_05 = "config/extraGore/enable_gibs_yellow_05.aoc";
                            const string enable_gibs_yellow_06 = "config/extraGore/enable_gibs_yellow_06.aoc";
                            const string enable_gibs_zombie_01 = "config/extraGore/enable_gibs_zombie_01.aoc";
                            const string enable_gibs_zombie_02 = "config/extraGore/enable_gibs_zombie_02.aoc";
                            const string enable_gibs_zombie_03 = "config/extraGore/enable_gibs_zombie_03.aoc";
                            const string enable_gibs_zombie_04 = "config/extraGore/enable_gibs_zombie_04.aoc";
                            const string enable_gibs_zombie_05 = "config/extraGore/enable_gibs_zombie_05.aoc";
                            const string enable_gibs_zombie_06 = "config/extraGore/enable_gibs_zombie_06.aoc";
                            const string enable_blood_impact_spider = "config/extraGore/enable_blood_impact_spider.pet";
                            const string enable_blood_impact_zombie = "config/extraGore/enable_blood_impact_zombie.pet";
                            const string enable_blood1_impact_spider = "config/extraGore/enable_blood1_impact_spider.pet";
                            const string enable_blood1_impact_zombie = "config/extraGore/enable_blood1_impact_zombie.pet";
                            const string enable_blood2_impact_spider = "config/extraGore/enable_blood2_impact_spider.pet";
                            const string enable_blood2_impact_zombie = "config/extraGore/enable_blood2_impact_zombie.pet";
                            const string enable_Green = "config/extraGore/enable_Green.pet";
                            const string enable_Orange = "config/extraGore/enable_Orange.pet";
                            const string enable_Red = "config/extraGore/enable_Red.pet";
                            const string enable_critical_strikes_red = "config/extraGore/first_blood/red/enable_critical_strikes_red.pet";
                            const string enable_red_blood = "config/extraGore/first_blood/red/enable_red_blood.pet";
                            const string enable_critical_strikes_green = "config/extraGore/first_blood/spider/enable_critical_strikes_green.pet";
                            const string enable_spider_blood = "config/extraGore/first_blood/spider/enable_spider_blood.pet";
                            const string enable_critical_strikes_zombie = "config/extraGore/first_blood/zombie/enable_critical_strikes_zombie.pet";
                            const string enable_zombie_blood = "config/extraGore/first_blood/zombie/enable_zombie_blood.pet";
                            const string enable_new_critical_strikes_green = "config/extraGore/new/enable_critical_strikes_green.pet";
                            const string enable_new_critical_strikes_orange = "config/extraGore/new/enable_critical_strikes_orange.pet";
                            const string enable_new_critical_strikes_red = "config/extraGore/new/enable_critical_strikes_red.pet";
                            const string enable_new_blood_impact_spider = "config/extraGore/new/green/enable_blood_impact_spider.pet";
                            const string enable_new_blood1_impact_spider = "config/extraGore/new/green/enable_blood1_impact_spider.pet";
                            const string enable_new_blood_impact = "config/extraGore/new/red/enable_blood_impact.pet";
                            const string enable_new_blood1_impact = "config/extraGore/new/red/enable_blood1_impact.pet";
                            const string enable_new_blood_impact_zombie = "config/extraGore/new/zombie/enable_blood_impact_zombie.pet";
                            const string enable_new_blood1_impact_zombie = "config/extraGore/new/zombie/enable_blood1_impact_zombie.pet";

                            const string blood_gibs_01 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_01.aoc";
                            const string blood_gibs_02 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_02.aoc";
                            const string blood_gibs_03 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_03.aoc";
                            const string blood_gibs_04 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_04.aoc";
                            const string blood_gibs_05 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_05.aoc";
                            const string blood_gibs_06 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_gibs_06.aoc";
                            const string gibs_yellow_01 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_01.aoc";
                            const string gibs_yellow_02 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_02.aoc";
                            const string gibs_yellow_03 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_03.aoc";
                            const string gibs_yellow_04 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_04.aoc";
                            const string gibs_yellow_05 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_05.aoc";
                            const string gibs_yellow_06 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_yellow_06.aoc";
                            const string gibs_zombie_01 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_01.aoc";
                            const string gibs_zombie_02 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_02.aoc";
                            const string gibs_zombie_03 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_03.aoc";
                            const string gibs_zombie_04 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_04.aoc";
                            const string gibs_zombie_05 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_05.aoc";
                            const string gibs_zombie_06 = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\gibs_zombie_06.aoc";
                            const string blood_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_impact_spider.pet";
                            const string blood_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood_impact_zombie.pet";
                            const string blood1_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood1_impact_spider.pet";
                            const string blood1_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood1_impact_zombie.pet";
                            const string blood2_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood2_impact_spider.pet";
                            const string blood2_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\blood2_impact_zombie.pet";
                            const string Green = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\Green.pet";
                            const string Orange = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\Orange.pet";
                            const string Red = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\Red.pet";
                            const string critical_strikes_red = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\red\\critical_strikes_red.pet";
                            const string red_blood = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\red\\red_blood.pet";
                            const string critical_strikes_green = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\spider\\critical_strikes_green.pet";
                            const string spider_blood = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\spider\\spider_blood.pet";
                            const string critical_strikes_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\zombie\\critical_strikes_zombie.pet";
                            const string zombie_blood = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\first_blood\\zombie\\zombie_blood.pet";
                            const string new_critical_strikes_green = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\critical_strikes_green.pet";
                            const string new_critical_strikes_orange = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\critical_strikes_orange.pet";
                            const string new_critical_strikes_red = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\critical_strikes_red.pet";
                            const string new_blood_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\green\\blood_impact_spider.pet";
                            const string new_blood1_impact_spider = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\green\\blood1_impact_spider.pet";
                            const string new_blood_impact = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\red\\blood_impact.pet";
                            const string new_blood1_impact = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\red\\blood1_impact.pet";
                            const string new_blood_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\zombie\\blood_impact_zombie.pet";
                            const string new_blood1_impact_zombie = "Metadata\\Effects\\Microtransactions\\Misc\\Extra_Blood\\new\\zombie\\blood1_impact_zombie.pet";

                            RecordsByPath[blood_gibs_01].ReplaceContents(ggpkPath, enable_blood_gibs_01, content.FreeRoot);
                            RecordsByPath[blood_gibs_02].ReplaceContents(ggpkPath, enable_blood_gibs_02, content.FreeRoot);
                            RecordsByPath[blood_gibs_03].ReplaceContents(ggpkPath, enable_blood_gibs_03, content.FreeRoot);
                            RecordsByPath[blood_gibs_04].ReplaceContents(ggpkPath, enable_blood_gibs_04, content.FreeRoot);
                            RecordsByPath[blood_gibs_05].ReplaceContents(ggpkPath, enable_blood_gibs_05, content.FreeRoot);
                            RecordsByPath[blood_gibs_06].ReplaceContents(ggpkPath, enable_blood_gibs_06, content.FreeRoot);
                            RecordsByPath[gibs_yellow_01].ReplaceContents(ggpkPath, enable_gibs_yellow_01, content.FreeRoot);
                            RecordsByPath[gibs_yellow_02].ReplaceContents(ggpkPath, enable_gibs_yellow_02, content.FreeRoot);
                            RecordsByPath[gibs_yellow_03].ReplaceContents(ggpkPath, enable_gibs_yellow_03, content.FreeRoot);
                            RecordsByPath[gibs_yellow_04].ReplaceContents(ggpkPath, enable_gibs_yellow_04, content.FreeRoot);
                            RecordsByPath[gibs_yellow_05].ReplaceContents(ggpkPath, enable_gibs_yellow_05, content.FreeRoot);
                            RecordsByPath[gibs_yellow_06].ReplaceContents(ggpkPath, enable_gibs_yellow_06, content.FreeRoot);
                            RecordsByPath[gibs_zombie_01].ReplaceContents(ggpkPath, enable_gibs_zombie_01, content.FreeRoot);
                            RecordsByPath[gibs_zombie_02].ReplaceContents(ggpkPath, enable_gibs_zombie_02, content.FreeRoot);
                            RecordsByPath[gibs_zombie_03].ReplaceContents(ggpkPath, enable_gibs_zombie_03, content.FreeRoot);
                            RecordsByPath[gibs_zombie_04].ReplaceContents(ggpkPath, enable_gibs_zombie_04, content.FreeRoot);
                            RecordsByPath[gibs_zombie_05].ReplaceContents(ggpkPath, enable_gibs_zombie_05, content.FreeRoot);
                            RecordsByPath[gibs_zombie_06].ReplaceContents(ggpkPath, enable_gibs_zombie_06, content.FreeRoot);
                            RecordsByPath[blood_impact_spider].ReplaceContents(ggpkPath, enable_blood_impact_spider, content.FreeRoot);
                            RecordsByPath[blood_impact_zombie].ReplaceContents(ggpkPath, enable_blood_impact_zombie, content.FreeRoot);
                            RecordsByPath[blood1_impact_spider].ReplaceContents(ggpkPath, enable_blood1_impact_spider, content.FreeRoot);
                            RecordsByPath[blood1_impact_zombie].ReplaceContents(ggpkPath, enable_blood1_impact_zombie, content.FreeRoot);
                            RecordsByPath[blood2_impact_spider].ReplaceContents(ggpkPath, enable_blood2_impact_spider, content.FreeRoot);
                            RecordsByPath[blood2_impact_zombie].ReplaceContents(ggpkPath, enable_blood2_impact_zombie, content.FreeRoot);
                            RecordsByPath[Green].ReplaceContents(ggpkPath, enable_Green, content.FreeRoot);
                            RecordsByPath[Orange].ReplaceContents(ggpkPath, enable_Orange, content.FreeRoot);
                            RecordsByPath[Red].ReplaceContents(ggpkPath, enable_Red, content.FreeRoot);
                            RecordsByPath[critical_strikes_red].ReplaceContents(ggpkPath, enable_critical_strikes_red, content.FreeRoot);
                            RecordsByPath[red_blood].ReplaceContents(ggpkPath, enable_red_blood, content.FreeRoot);
                            RecordsByPath[critical_strikes_green].ReplaceContents(ggpkPath, enable_critical_strikes_green, content.FreeRoot);
                            RecordsByPath[spider_blood].ReplaceContents(ggpkPath, enable_spider_blood, content.FreeRoot);
                            RecordsByPath[critical_strikes_zombie].ReplaceContents(ggpkPath, enable_critical_strikes_zombie, content.FreeRoot);
                            RecordsByPath[zombie_blood].ReplaceContents(ggpkPath, enable_zombie_blood, content.FreeRoot);
                            RecordsByPath[new_critical_strikes_green].ReplaceContents(ggpkPath, enable_new_critical_strikes_green, content.FreeRoot);
                            RecordsByPath[new_critical_strikes_orange].ReplaceContents(ggpkPath, enable_new_critical_strikes_orange, content.FreeRoot);
                            RecordsByPath[new_critical_strikes_red].ReplaceContents(ggpkPath, enable_new_critical_strikes_red, content.FreeRoot);
                            RecordsByPath[new_blood_impact_spider].ReplaceContents(ggpkPath, enable_new_blood_impact_spider, content.FreeRoot);
                            RecordsByPath[new_blood1_impact_spider].ReplaceContents(ggpkPath, enable_new_blood1_impact_spider, content.FreeRoot);
                            RecordsByPath[new_blood_impact].ReplaceContents(ggpkPath, enable_new_blood_impact, content.FreeRoot);
                            RecordsByPath[new_blood1_impact].ReplaceContents(ggpkPath, enable_new_blood1_impact, content.FreeRoot);
                            RecordsByPath[new_blood_impact_zombie].ReplaceContents(ggpkPath, enable_new_blood_impact_zombie, content.FreeRoot);
                            RecordsByPath[new_blood1_impact_zombie].ReplaceContents(ggpkPath, enable_new_blood1_impact_zombie, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CorruptedArea()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (corruptedArea.IsChecked)
                {
                    case true:
                        {
                            const string disable_air_particles = "config/corruptedArea/disable_air_particles.pet";
                            const string disable_directionalfog = "config/corruptedArea/disable_directionalfog.pet";
                            const string disable_fog_circle = "config/corruptedArea/disable_fog_circle.pet";
                            const string disable_fog_cyl = "config/corruptedArea/disable_fog_cyl.pet";
                            const string disable_ground_fog = "config/corruptedArea/disable_ground_fog.pet";
                            const string disable_smoke_back_filler = "config/corruptedArea/disable_smoke_back_filler.pet";
                            const string disable_smoke_rollout = "config/corruptedArea/disable_smoke_rollout.pet";
                            const string disable_smoke_rollout_bigbox = "config/corruptedArea/disable_smoke_rollout_bigbox.pet";

                            const string air_particles = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\air_particles.pet";
                            const string directionalfog = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\directionalfog.pet";
                            const string fog_circle = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\fog_circle.pet";
                            const string fog_cyl = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\fog_cyl.pet";
                            const string ground_fog = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\ground_fog.pet";
                            const string smoke_back_filler = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\smoke_back_filler.pet";
                            const string smoke_rollout = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\smoke_rollout.pet";
                            const string smoke_rollout_bigbox = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\smoke_rollout_bigbox.pet";

                            RecordsByPath[air_particles].ReplaceContents(ggpkPath, disable_air_particles, content.FreeRoot);
                            RecordsByPath[directionalfog].ReplaceContents(ggpkPath, disable_directionalfog, content.FreeRoot);
                            RecordsByPath[fog_circle].ReplaceContents(ggpkPath, disable_fog_circle, content.FreeRoot);
                            RecordsByPath[fog_cyl].ReplaceContents(ggpkPath, disable_fog_cyl, content.FreeRoot);
                            RecordsByPath[ground_fog].ReplaceContents(ggpkPath, disable_ground_fog, content.FreeRoot);
                            RecordsByPath[smoke_back_filler].ReplaceContents(ggpkPath, disable_smoke_back_filler, content.FreeRoot);
                            RecordsByPath[smoke_rollout].ReplaceContents(ggpkPath, disable_smoke_rollout, content.FreeRoot);
                            RecordsByPath[smoke_rollout_bigbox].ReplaceContents(ggpkPath, disable_smoke_rollout_bigbox, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_air_particles = "config/corruptedArea/enable_air_particles.pet";
                            const string enable_directionalfog = "config/corruptedArea/enable_directionalfog.pet";
                            const string enable_fog_circle = "config/corruptedArea/enable_fog_circle.pet";
                            const string enable_fog_cyl = "config/corruptedArea/enable_fog_cyl.pet";
                            const string enable_ground_fog = "config/corruptedArea/enable_ground_fog.pet";
                            const string enable_smoke_back_filler = "config/corruptedArea/enable_smoke_back_filler.pet";
                            const string enable_smoke_rollout = "config/corruptedArea/enable_smoke_rollout.pet";

                            const string enable_smoke_rollout_bigbox = "config/corruptedArea/enable_smoke_rollout_bigbox.pet";
                            const string air_particles = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\air_particles.pet";
                            const string directionalfog = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\directionalfog.pet";
                            const string fog_circle = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\fog_circle.pet";
                            const string fog_cyl = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\fog_cyl.pet";
                            const string ground_fog = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\ground_fog.pet";
                            const string smoke_back_filler = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\smoke_back_filler.pet";
                            const string smoke_rollout = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\smoke_rollout.pet";
                            const string smoke_rollout_bigbox = "Metadata\\Particles\\enviro_effects\\vaal_sidearea_effects\\smoke_rollout_bigbox.pet";

                            RecordsByPath[air_particles].ReplaceContents(ggpkPath, enable_air_particles, content.FreeRoot);
                            RecordsByPath[directionalfog].ReplaceContents(ggpkPath, enable_directionalfog, content.FreeRoot);
                            RecordsByPath[fog_circle].ReplaceContents(ggpkPath, enable_fog_circle, content.FreeRoot);
                            RecordsByPath[fog_cyl].ReplaceContents(ggpkPath, enable_fog_cyl, content.FreeRoot);
                            RecordsByPath[ground_fog].ReplaceContents(ggpkPath, enable_ground_fog, content.FreeRoot);
                            RecordsByPath[smoke_back_filler].ReplaceContents(ggpkPath, enable_smoke_back_filler, content.FreeRoot);
                            RecordsByPath[smoke_rollout].ReplaceContents(ggpkPath, enable_smoke_rollout, content.FreeRoot);
                            RecordsByPath[smoke_rollout_bigbox].ReplaceContents(ggpkPath, enable_smoke_rollout_bigbox, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MonsterSounds()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (monsterSounds.IsChecked)
                {
                    case true:
                        {
                            const string disable_ChaosElemental = "config/monsterSounds/disable_ChaosElemental.aoc";
                            const string disable_ChaosElementalSummoned = "config/monsterSounds/disable_ChaosElementalSummoned.aoc";
                            const string disable_FireElemental = "config/monsterSounds/disable_FireElemental.aoc";
                            const string disable_FireElementalSummoned = "config/monsterSounds/disable_FireElementalSummoned.aoc";
                            const string disable_IceElemental = "config/monsterSounds/disable_IceElemental.aoc";
                            const string disable_IceElementalSummoned = "config/monsterSounds/disable_IceElementalSummoned.aoc";
                            const string disable_Revenant = "config/monsterSounds/disable_Revenant.aoc";
                            const string disable_RevenantBoss = "config/monsterSounds/disable_RevenantBoss.aoc";

                            const string ChaosElemental = "Metadata\\Monsters\\ChaosElemental\\ChaosElemental.aoc";
                            const string ChaosElementalSummoned = "Metadata\\Monsters\\ChaosElemental\\ChaosElementalSummoned.aoc";
                            const string FireElemental = "Metadata\\Monsters\\FireElemental\\FireElemental.aoc";
                            const string FireElementalSummoned = "Metadata\\Monsters\\FireElemental\\FireElementalSummoned.aoc";
                            const string IceElemental = "Metadata\\Monsters\\IceElemental\\IceElemental.aoc";
                            const string IceElementalSummoned = "Metadata\\Monsters\\IceElemental\\IceElementalSummoned.aoc";
                            const string Revenant = "Metadata\\Monsters\\Revenant\\Revenant.aoc";
                            const string RevenantBoss = "Metadata\\Monsters\\Revenant\\RevenantBoss.aoc";

                            RecordsByPath[ChaosElemental].ReplaceContents(ggpkPath, disable_ChaosElemental, content.FreeRoot);
                            RecordsByPath[ChaosElementalSummoned].ReplaceContents(ggpkPath, disable_ChaosElementalSummoned, content.FreeRoot);
                            RecordsByPath[FireElemental].ReplaceContents(ggpkPath, disable_FireElemental, content.FreeRoot);
                            RecordsByPath[FireElementalSummoned].ReplaceContents(ggpkPath, disable_FireElementalSummoned, content.FreeRoot);
                            RecordsByPath[IceElemental].ReplaceContents(ggpkPath, disable_IceElemental, content.FreeRoot);
                            RecordsByPath[IceElementalSummoned].ReplaceContents(ggpkPath, disable_IceElementalSummoned, content.FreeRoot);
                            RecordsByPath[Revenant].ReplaceContents(ggpkPath, disable_Revenant, content.FreeRoot);
                            RecordsByPath[RevenantBoss].ReplaceContents(ggpkPath, disable_RevenantBoss, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_ChaosElemental = "config/monsterSounds/enable_ChaosElemental.aoc";
                            const string enable_ChaosElementalSummoned = "config/monsterSounds/enable_ChaosElementalSummoned.aoc";
                            const string enable_FireElemental = "config/monsterSounds/enable_FireElemental.aoc";
                            const string enable_FireElementalSummoned = "config/monsterSounds/enable_FireElementalSummoned.aoc";
                            const string enable_IceElemental = "config/monsterSounds/enable_IceElemental.aoc";
                            const string enable_IceElementalSummoned = "config/monsterSounds/enable_IceElementalSummoned.aoc";
                            const string enable_Revenant = "config/monsterSounds/enable_Revenant.aoc";
                            const string enable_RevenantBoss = "config/monsterSounds/enable_RevenantBoss.aoc";

                            const string ChaosElemental = "Metadata\\Monsters\\ChaosElemental\\ChaosElemental.aoc";
                            const string ChaosElementalSummoned = "Metadata\\Monsters\\ChaosElemental\\ChaosElementalSummoned.aoc";
                            const string FireElemental = "Metadata\\Monsters\\FireElemental\\FireElemental.aoc";
                            const string FireElementalSummoned = "Metadata\\Monsters\\FireElemental\\FireElementalSummoned.aoc";
                            const string IceElemental = "Metadata\\Monsters\\IceElemental\\IceElemental.aoc";
                            const string IceElementalSummoned = "Metadata\\Monsters\\IceElemental\\IceElementalSummoned.aoc";
                            const string Revenant = "Metadata\\Monsters\\Revenant\\Revenant.aoc";
                            const string RevenantBoss = "Metadata\\Monsters\\Revenant\\RevenantBoss.aoc";

                            RecordsByPath[ChaosElemental].ReplaceContents(ggpkPath, enable_ChaosElemental, content.FreeRoot);
                            RecordsByPath[ChaosElementalSummoned].ReplaceContents(ggpkPath, enable_ChaosElementalSummoned, content.FreeRoot);
                            RecordsByPath[FireElemental].ReplaceContents(ggpkPath, enable_FireElemental, content.FreeRoot);
                            RecordsByPath[FireElementalSummoned].ReplaceContents(ggpkPath, enable_FireElementalSummoned, content.FreeRoot);
                            RecordsByPath[IceElemental].ReplaceContents(ggpkPath, enable_IceElemental, content.FreeRoot);
                            RecordsByPath[IceElementalSummoned].ReplaceContents(ggpkPath, enable_IceElementalSummoned, content.FreeRoot);
                            RecordsByPath[Revenant].ReplaceContents(ggpkPath, enable_Revenant, content.FreeRoot);
                            RecordsByPath[RevenantBoss].ReplaceContents(ggpkPath, enable_RevenantBoss, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CharSounds()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (charSounds.IsChecked)
                {
                    case true:
                        {
                            const string disable_Dex = "config/charSounds/disable_Dex.aoc";
                            const string disable_DexInt = "config/charSounds/disable_DexInt.aoc";
                            const string disable_Int = "config/charSounds/disable_Int.aoc";
                            const string disable_Str = "config/charSounds/disable_Str.aoc";
                            const string disable_StrDex = "config/charSounds/disable_StrDex.aoc";
                            const string disable_StrDexInt = "config/charSounds/disable_StrDexInt.aoc";
                            const string disable_StrInt = "config/charSounds/disable_StrInt.aoc";

                            const string Dex = "Metadata\\Characters\\Dex\\Dex.aoc";
                            const string DexInt = "Metadata\\Characters\\DexInt\\DexInt.aoc";
                            const string Int = "Metadata\\Characters\\Int\\Int.aoc";
                            const string Str = "Metadata\\Characters\\Str\\Str.aoc";
                            const string StrDex = "Metadata\\Characters\\StrDex\\StrDex.aoc";
                            const string StrDexInt = "Metadata\\Characters\\StrDexInt\\StrDexInt.aoc";
                            const string StrInt = "Metadata\\Characters\\StrInt\\StrInt.aoc";

                            RecordsByPath[Dex].ReplaceContents(ggpkPath, disable_Dex, content.FreeRoot);
                            RecordsByPath[DexInt].ReplaceContents(ggpkPath, disable_DexInt, content.FreeRoot);
                            RecordsByPath[Int].ReplaceContents(ggpkPath, disable_Int, content.FreeRoot);
                            RecordsByPath[Str].ReplaceContents(ggpkPath, disable_Str, content.FreeRoot);
                            RecordsByPath[StrDex].ReplaceContents(ggpkPath, disable_StrDex, content.FreeRoot);
                            RecordsByPath[StrDexInt].ReplaceContents(ggpkPath, disable_StrDexInt, content.FreeRoot);
                            RecordsByPath[StrInt].ReplaceContents(ggpkPath, disable_StrInt, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_Dex = "config/charSounds/enable_Dex.aoc";
                            const string enable_DexInt = "config/charSounds/enable_DexInt.aoc";
                            const string enable_Int = "config/charSounds/enable_Int.aoc";
                            const string enable_Str = "config/charSounds/enable_Str.aoc";
                            const string enable_StrDex = "config/charSounds/enable_StrDex.aoc";
                            const string enable_StrDexInt = "config/charSounds/enable_StrDexInt.aoc";
                            const string enable_StrInt = "config/charSounds/enable_StrInt.aoc";

                            const string Dex = "Metadata\\Characters\\Dex\\Dex.aoc";
                            const string DexInt = "Metadata\\Characters\\DexInt\\DexInt.aoc";
                            const string Int = "Metadata\\Characters\\Int\\Int.aoc";
                            const string Str = "Metadata\\Characters\\Str\\Str.aoc";
                            const string StrDex = "Metadata\\Characters\\StrDex\\StrDex.aoc";
                            const string StrDexInt = "Metadata\\Characters\\StrDexInt\\StrDexInt.aoc";
                            const string StrInt = "Metadata\\Characters\\StrInt\\StrInt.aoc";

                            RecordsByPath[Dex].ReplaceContents(ggpkPath, enable_Dex, content.FreeRoot);
                            RecordsByPath[DexInt].ReplaceContents(ggpkPath, enable_DexInt, content.FreeRoot);
                            RecordsByPath[Int].ReplaceContents(ggpkPath, enable_Int, content.FreeRoot);
                            RecordsByPath[Str].ReplaceContents(ggpkPath, enable_Str, content.FreeRoot);
                            RecordsByPath[StrDex].ReplaceContents(ggpkPath, enable_StrDex, content.FreeRoot);
                            RecordsByPath[StrDexInt].ReplaceContents(ggpkPath, enable_StrDexInt, content.FreeRoot);
                            RecordsByPath[StrInt].ReplaceContents(ggpkPath, enable_StrInt, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChargeSounds()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (chargeSounds.IsChecked)
                {
                    case true:
                        {
                            const string disable_arctic_armor = "config/skillSounds/disable_arctic_armor.aoc";
                            const string disable_power_charge = "config/chargeSounds/disable_rig_charge.aoc";
                            const string disable_endurance_charge = "config/chargeSounds/disable_rig_endurance.aoc";
                            const string disable_frenzy_charge = "config/chargeSounds/disable_rig_frenzy.aoc";

                            const string arctic_armor = "Metadata\\Effects\\Spells\\frozen_barrier\\rig.aoc";
                            const string power_charge = "Metadata\\Effects\\Spells\\orbs\\rig_charge.aoc";
                            const string endurance_charge = "Metadata\\Effects\\Spells\\orbs\\rig_endurance.aoc";
                            const string frenzy_charge = "Metadata\\Effects\\Spells\\orbs\\rig_frenzy.aoc";

                            RecordsByPath[arctic_armor].ReplaceContents(ggpkPath, disable_arctic_armor, content.FreeRoot);
                            RecordsByPath[power_charge].ReplaceContents(ggpkPath, disable_power_charge, content.FreeRoot);
                            RecordsByPath[endurance_charge].ReplaceContents(ggpkPath, disable_endurance_charge, content.FreeRoot);
                            RecordsByPath[frenzy_charge].ReplaceContents(ggpkPath, disable_frenzy_charge, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_arctic_armor = "config/skillSounds/enable_arctic_armor.aoc";
                            const string enable_power_charge = "config/chargeSounds/enable_rig_charge.aoc";
                            const string enable_endurance_charge = "config/chargeSounds/enable_rig_endurance.aoc";
                            const string enable_frenzy_charge = "config/chargeSounds/enable_rig_frenzy.aoc";

                            const string arctic_armor = "Metadata\\Effects\\Spells\\frozen_barrier\\rig.aoc";
                            const string power_charge = "Metadata\\Effects\\Spells\\orbs\\rig_charge.aoc";
                            const string endurance_charge = "Metadata\\Effects\\Spells\\orbs\\rig_endurance.aoc";
                            const string frenzy_charge = "Metadata\\Effects\\Spells\\orbs\\rig_frenzy.aoc";

                            RecordsByPath[arctic_armor].ReplaceContents(ggpkPath, enable_arctic_armor, content.FreeRoot);
                            RecordsByPath[power_charge].ReplaceContents(ggpkPath, enable_power_charge, content.FreeRoot);
                            RecordsByPath[endurance_charge].ReplaceContents(ggpkPath, enable_endurance_charge, content.FreeRoot);
                            RecordsByPath[frenzy_charge].ReplaceContents(ggpkPath, enable_frenzy_charge, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), 
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PortalSounds()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (portalSounds.IsChecked)
                {
                    case true:
                        {
                            const string disable_rig = "config/portalSounds/disable_rig.aoc";
                            const string disable_waypoint_idle = "config/portalSounds/disable_waypoint_idle.aoc";

                            const string rig = "Metadata\\Effects\\Environment\\act3\\gem_lady_portal\\rig.aoc";
                            const string waypoint_idle = "Metadata\\Effects\\Environment\\waypoint\\new\\waypoint_idle.aoc";

                            RecordsByPath[rig].ReplaceContents(ggpkPath, disable_rig, content.FreeRoot);
                            RecordsByPath[waypoint_idle].ReplaceContents(ggpkPath, disable_waypoint_idle, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_rig = "config/portalSounds/enable_rig.aoc";
                            const string enable_waypoint_idle = "config/portalSounds/enable_waypoint_idle.aoc";

                            const string rig = "Metadata\\Effects\\Environment\\act3\\gem_lady_portal\\rig.aoc";
                            const string waypoint_idle = "Metadata\\Effects\\Environment\\waypoint\\new\\waypoint_idle.aoc";

                            RecordsByPath[rig].ReplaceContents(ggpkPath, enable_rig, content.FreeRoot);
                            RecordsByPath[waypoint_idle].ReplaceContents(ggpkPath, enable_waypoint_idle, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HeraldOfIce()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (heraldOfIce.IsChecked)
                {
                    case true:
                        {
                            const string disable_circle = "config/skillEffects/herald/ice/explode/disable_circle.pet";
                            const string disable_cyl = "config/skillEffects/herald/ice/explode/disable_cyl.pet";
                            const string disable_mid = "config/skillEffects/herald/ice/explode/disable_mid.pet";

                            const string circle = "Metadata\\Particles\\herald\\ice\\explode\\circle.pet";
                            const string cyl = "Metadata\\Particles\\herald\\ice\\explode\\cyl.pet";
                            const string mid = "Metadata\\Particles\\herald\\ice\\explode\\mid.pet";

                            RecordsByPath[circle].ReplaceContents(ggpkPath, disable_circle, content.FreeRoot);
                            RecordsByPath[cyl].ReplaceContents(ggpkPath, disable_cyl, content.FreeRoot);
                            RecordsByPath[mid].ReplaceContents(ggpkPath, disable_mid, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_circle = "config/skillEffects/herald/ice/explode/enable_circle.pet";
                            const string enable_cyl = "config/skillEffects/herald/ice/explode/enable_cyl.pet";
                            const string enable_mid = "config/skillEffects/herald/ice/explode//enable_mid.pet";

                            const string circle = "Metadata\\Particles\\herald\\ice\\explode\\circle.pet";
                            const string cyl = "Metadata\\Particles\\herald\\ice\\explode\\cyl.pet";
                            const string mid = "Metadata\\Particles\\herald\\ice\\explode\\mid.pet";

                            RecordsByPath[circle].ReplaceContents(ggpkPath, enable_circle, content.FreeRoot);
                            RecordsByPath[cyl].ReplaceContents(ggpkPath, enable_cyl, content.FreeRoot);
                            RecordsByPath[mid].ReplaceContents(ggpkPath, enable_mid, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DischargeImproved()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (dischargeImproved.IsChecked)
                {
                    case true:
                        {
                            const string disable_fire_actual_line = "config/skillEffects/dischargeImproved/fire/disable_actual_line.pet";
                            const string disable_botexplode = "config/skillEffects/dischargeImproved/fire/disable_botexplode.pet";
                            const string disable_cyl = "config/skillEffects/dischargeImproved/fire/disable_cyl.pet";
                            const string disable_line = "config/skillEffects/dischargeImproved/fire/disable_line.pet";
                            const string disable_ice_botexplode = "config/skillEffects/dischargeImproved/ice/disable_botexplode.pet";
                            const string disable_ice_botmist = "config/skillEffects/dischargeImproved/ice/disable_botmist.pet";
                            const string disable_ice_line = "config/skillEffects/dischargeImproved/ice/disable_line.pet";
                            const string disable_ice_lineactual = "config/skillEffects/dischargeImproved/ice/disable_lineactual.pet";
                            const string disable_light_botexplode = "config/skillEffects/dischargeImproved/light/disable_botexplode.pet";
                            const string disable_light_cyl = "config/skillEffects/dischargeImproved/light/disable_cyl.pet";
                            const string disable_light_light_linger = "config/skillEffects/dischargeImproved/light/disable_light_linger.pet";
                            const string disable_light_line = "config/skillEffects/dischargeImproved/light/disable_line.pet";
                            const string disable_fire_discharge = "config/skillEffects/dischargeImproved/disable_fire_discharge.aoc";
                            const string disable_fire_discharge_NoSound = "config/skillEffects/dischargeImproved/disable_fire_discharge_NoSound.aoc";
                            const string disable_light_discharge = "config/skillEffects/dischargeImproved/disable_light_discharge.aoc";
                            const string disable_light_discharge_NoSound = "config/skillEffects/dischargeImproved/disable_light_discharge_NoSound.aoc";
                            const string disable_ice_discharge = "config/skillEffects/dischargeImproved/disable_ice_discharge.aoc";
                            const string disable_ice_discharge_NoSound = "config/skillEffects/dischargeImproved/disable_ice_discharge_NoSound.aoc";

                            const string fire_actual_line = "Metadata\\Particles\\discharge\\fire\\actual_line.pet";
                            const string botexplode = "Metadata\\Particles\\discharge\\fire\\botexplode.pet";
                            const string cyl = "Metadata\\Particles\\discharge\\fire\\cyl.pet";
                            const string line = "Metadata\\Particles\\discharge\\fire\\line.pet";
                            const string ice_botexplode = "Metadata\\Particles\\discharge\\ice\\botexplode.pet";
                            const string ice_botmist = "Metadata\\Particles\\discharge\\ice\\botmist.pet";
                            const string ice_line = "Metadata\\Particles\\discharge\\ice\\line.pet";
                            const string ice_lineactual = "Metadata\\Particles\\discharge\\ice\\lineactual.pet";
                            const string light_botexplode = "Metadata\\Particles\\discharge\\light\\botexplode.pet";
                            const string light_cyl = "Metadata\\Particles\\discharge\\light\\cyl.pet";
                            const string light_light_linger = "Metadata\\Particles\\discharge\\light\\light_linger.pet";
                            const string light_line = "Metadata\\Particles\\discharge\\light\\line.pet";
                            const string fire_discharge = "Metadata\\Effects\\Spells\\discharge\\fire_discharge.aoc";
                            const string fire_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\fire_discharge_NoSound.aoc";
                            const string light_discharge = "Metadata\\Effects\\Spells\\discharge\\light_discharge.aoc";
                            const string light_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\light_discharge_NoSound.aoc";
                            const string ice_discharge = "Metadata\\Effects\\Spells\\discharge\\ice_discharge.aoc";
                            const string ice_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\ice_discharge_NoSound.aoc";

                            RecordsByPath[fire_actual_line].ReplaceContents(ggpkPath, disable_fire_actual_line, content.FreeRoot);
                            RecordsByPath[botexplode].ReplaceContents(ggpkPath, disable_botexplode, content.FreeRoot);
                            RecordsByPath[cyl].ReplaceContents(ggpkPath, disable_cyl, content.FreeRoot);
                            RecordsByPath[line].ReplaceContents(ggpkPath, disable_line, content.FreeRoot);
                            RecordsByPath[ice_botexplode].ReplaceContents(ggpkPath, disable_ice_botexplode, content.FreeRoot);
                            RecordsByPath[ice_botmist].ReplaceContents(ggpkPath, disable_ice_botmist, content.FreeRoot);
                            RecordsByPath[ice_line].ReplaceContents(ggpkPath, disable_ice_line, content.FreeRoot);
                            RecordsByPath[ice_lineactual].ReplaceContents(ggpkPath, disable_ice_lineactual, content.FreeRoot);
                            RecordsByPath[light_botexplode].ReplaceContents(ggpkPath, disable_light_botexplode, content.FreeRoot);
                            RecordsByPath[light_cyl].ReplaceContents(ggpkPath, disable_light_cyl, content.FreeRoot);
                            RecordsByPath[light_light_linger].ReplaceContents(ggpkPath, disable_light_light_linger, content.FreeRoot);
                            RecordsByPath[light_line].ReplaceContents(ggpkPath, disable_light_line, content.FreeRoot);
                            RecordsByPath[fire_discharge].ReplaceContents(ggpkPath, disable_fire_discharge, content.FreeRoot);
                            RecordsByPath[fire_discharge_NoSound].ReplaceContents(ggpkPath, disable_fire_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[light_discharge].ReplaceContents(ggpkPath, disable_light_discharge, content.FreeRoot);
                            RecordsByPath[light_discharge_NoSound].ReplaceContents(ggpkPath, disable_light_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[ice_discharge].ReplaceContents(ggpkPath, disable_ice_discharge, content.FreeRoot);
                            RecordsByPath[ice_discharge_NoSound].ReplaceContents(ggpkPath, disable_ice_discharge_NoSound, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_fire_actual_line = "config/skillEffects/dischargeImproved/fire/enable_actual_line.pet";
                            const string enable_fire_botexplode = "config/skillEffects/dischargeImproved/fire/enable_botexplode.pet";
                            const string enable_fire_cyl = "config/skillEffects/dischargeImproved/fire/enable_cyl.pet";
                            const string enable_fire_line = "config/skillEffects/dischargeImproved/fire/enable_line.pet";
                            const string enable_ice_botexplode = "config/skillEffects/dischargeImproved/ice/enable_botexplode.pet";
                            const string enable_ice_botmist = "config/skillEffects/dischargeImproved/ice/enable_botmist.pet";
                            const string enable_ice_line = "config/skillEffects/dischargeImproved/ice/enable_line.pet";
                            const string enable_ice_lineactual = "config/skillEffects/dischargeImproved/ice/enable_lineactual.pet";
                            const string enable_light_botexplode = "config/skillEffects/dischargeImproved/light/enable_botexplode.pet";
                            const string enable_light_cyl = "config/skillEffects/dischargeImproved/light/enable_cyl.pet";
                            const string enable_light_light_linger = "config/skillEffects/dischargeImproved/light/enable_light_linger.pet";
                            const string enable_light_line = "config/skillEffects/dischargeImproved/light/enable_line.pet";
                            const string enable_fire_discharge = "config/skillEffects/dischargeImproved/enable_fire_discharge.aoc";
                            const string enable_fire_discharge_NoSound = "config/skillEffects/dischargeImproved/enable_fire_discharge_NoSound.aoc";
                            const string enable_light_discharge = "config/skillEffects/dischargeImproved/enable_light_discharge.aoc";
                            const string enable_light_discharge_NoSound = "config/skillEffects/dischargeImproved/enable_light_discharge_NoSound.aoc";
                            const string enable_ice_discharge = "config/skillEffects/dischargeImproved/enable_ice_discharge.aoc";
                            const string enable_ice_discharge_NoSound = "config/skillEffects/dischargeImproved/enable_ice_discharge_NoSound.aoc";

                            const string fire_actual_line = "Metadata\\Particles\\discharge\\fire\\actual_line.pet";
                            const string fire_botexplode = "Metadata\\Particles\\discharge\\fire\\botexplode.pet";
                            const string fire_cyl = "Metadata\\Particles\\discharge\\fire\\cyl.pet";
                            const string fire_line = "Metadata\\Particles\\discharge\\fire\\line.pet";
                            const string ice_botexplode = "Metadata\\Particles\\discharge\\ice\\botexplode.pet";
                            const string ice_botmist = "Metadata\\Particles\\discharge\\ice\\botmist.pet";
                            const string ice_line = "Metadata\\Particles\\discharge\\ice\\line.pet";
                            const string ice_lineactual = "Metadata\\Particles\\discharge\\ice\\lineactual.pet";
                            const string light_botexplode = "Metadata\\Particles\\discharge\\light\\botexplode.pet";
                            const string light_cyl = "Metadata\\Particles\\discharge\\light\\cyl.pet";
                            const string light_light_linger = "Metadata\\Particles\\discharge\\light\\light_linger.pet";
                            const string light_line = "Metadata\\Particles\\discharge\\light\\line.pet";
                            const string fire_discharge = "Metadata\\Effects\\Spells\\discharge\\fire_discharge.aoc";
                            const string fire_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\fire_discharge_NoSound.aoc";
                            const string light_discharge = "Metadata\\Effects\\Spells\\discharge\\light_discharge.aoc";
                            const string light_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\light_discharge_NoSound.aoc";
                            const string ice_discharge = "Metadata\\Effects\\Spells\\discharge\\ice_discharge.aoc";
                            const string ice_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\ice_discharge_NoSound.aoc";

                            RecordsByPath[fire_actual_line].ReplaceContents(ggpkPath, enable_fire_actual_line, content.FreeRoot);
                            RecordsByPath[fire_botexplode].ReplaceContents(ggpkPath, enable_fire_botexplode, content.FreeRoot);
                            RecordsByPath[fire_cyl].ReplaceContents(ggpkPath, enable_fire_cyl, content.FreeRoot);
                            RecordsByPath[fire_line].ReplaceContents(ggpkPath, enable_fire_line, content.FreeRoot);
                            RecordsByPath[ice_botexplode].ReplaceContents(ggpkPath, enable_ice_botexplode, content.FreeRoot);
                            RecordsByPath[ice_botmist].ReplaceContents(ggpkPath, enable_ice_botmist, content.FreeRoot);
                            RecordsByPath[ice_line].ReplaceContents(ggpkPath, enable_ice_line, content.FreeRoot);
                            RecordsByPath[ice_lineactual].ReplaceContents(ggpkPath, enable_ice_lineactual, content.FreeRoot);
                            RecordsByPath[light_botexplode].ReplaceContents(ggpkPath, enable_light_botexplode, content.FreeRoot);
                            RecordsByPath[light_cyl].ReplaceContents(ggpkPath, enable_light_cyl, content.FreeRoot);
                            RecordsByPath[light_light_linger].ReplaceContents(ggpkPath, enable_light_light_linger, content.FreeRoot);
                            RecordsByPath[light_line].ReplaceContents(ggpkPath, enable_light_line, content.FreeRoot);
                            RecordsByPath[fire_discharge].ReplaceContents(ggpkPath, enable_fire_discharge, content.FreeRoot);
                            RecordsByPath[fire_discharge_NoSound].ReplaceContents(ggpkPath, enable_fire_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[light_discharge].ReplaceContents(ggpkPath, enable_light_discharge, content.FreeRoot);
                            RecordsByPath[light_discharge_NoSound].ReplaceContents(ggpkPath, enable_light_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[ice_discharge].ReplaceContents(ggpkPath, enable_ice_discharge, content.FreeRoot);
                            RecordsByPath[ice_discharge_NoSound].ReplaceContents(ggpkPath, enable_ice_discharge_NoSound, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DischargeDisabled()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            try
            {
                switch (dischargeDisabled.IsChecked)
                {
                    case true:
                        {
                            const string disable_fire_actual_line = "config/skillEffects/dischargeDisabled/fire/disable_actual_line.pet";
                            const string disable_botexplode = "config/skillEffects/dischargeDisabled/fire/disable_botexplode.pet";
                            const string disable_cyl = "config/skillEffects/dischargeDisabled/fire/disable_cyl.pet";
                            const string disable_line = "config/skillEffects/dischargeDisabled/fire/disable_line.pet";
                            const string disable_ice_botexplode = "config/skillEffects/dischargeDisabled/ice/disable_botexplode.pet";
                            const string disable_ice_botmist = "config/skillEffects/dischargeDisabled/ice/disable_botmist.pet";
                            const string disable_ice_line = "config/skillEffects/dischargeDisabled/ice/disable_line.pet";
                            const string disable_ice_lineactual = "config/skillEffects/dischargeDisabled/ice/disable_lineactual.pet";
                            const string disable_light_botexplode = "config/skillEffects/dischargeDisabled/light/disable_botexplode.pet";
                            const string disable_light_cyl = "config/skillEffects/dischargeDisabled/light/disable_cyl.pet";
                            const string disable_light_light_linger = "config/skillEffects/dischargeDisabled/light/disable_light_linger.pet";
                            const string disable_light_line = "config/skillEffects/dischargeDisabled/light/disable_line.pet";
                            const string disable_fire_discharge = "config/skillEffects/dischargeDisabled/disable_fire_discharge.aoc";
                            const string disable_fire_discharge_NoSound = "config/skillEffects/dischargeDisabled/disable_fire_discharge_NoSound.aoc";
                            const string disable_light_discharge = "config/skillEffects/dischargeDisabled/disable_light_discharge.aoc";
                            const string disable_light_discharge_NoSound = "config/skillEffects/dischargeDisabled/disable_light_discharge_NoSound.aoc";
                            const string disable_ice_discharge = "config/skillEffects/dischargeDisabled/disable_ice_discharge.aoc";
                            const string disable_ice_discharge_NoSound = "config/skillEffects/dischargeDisabled/disable_ice_discharge_NoSound.aoc";

                            const string fire_actual_line = "Metadata\\Particles\\discharge\\fire\\actual_line.pet";
                            const string botexplode = "Metadata\\Particles\\discharge\\fire\\botexplode.pet";
                            const string cyl = "Metadata\\Particles\\discharge\\fire\\cyl.pet";
                            const string line = "Metadata\\Particles\\discharge\\fire\\line.pet";
                            const string ice_botexplode = "Metadata\\Particles\\discharge\\ice\\botexplode.pet";
                            const string ice_botmist = "Metadata\\Particles\\discharge\\ice\\botmist.pet";
                            const string ice_line = "Metadata\\Particles\\discharge\\ice\\line.pet";
                            const string ice_lineactual = "Metadata\\Particles\\discharge\\ice\\lineactual.pet";
                            const string light_botexplode = "Metadata\\Particles\\discharge\\light\\botexplode.pet";
                            const string light_cyl = "Metadata\\Particles\\discharge\\light\\cyl.pet";
                            const string light_light_linger = "Metadata\\Particles\\discharge\\light\\light_linger.pet";
                            const string light_line = "Metadata\\Particles\\discharge\\light\\line.pet";
                            const string fire_discharge = "Metadata\\Effects\\Spells\\discharge\\fire_discharge.aoc";
                            const string fire_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\fire_discharge_NoSound.aoc";
                            const string light_discharge = "Metadata\\Effects\\Spells\\discharge\\light_discharge.aoc";
                            const string light_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\light_discharge_NoSound.aoc";
                            const string ice_discharge = "Metadata\\Effects\\Spells\\discharge\\ice_discharge.aoc";
                            const string ice_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\ice_discharge_NoSound.aoc";

                            RecordsByPath[fire_actual_line].ReplaceContents(ggpkPath, disable_fire_actual_line, content.FreeRoot);
                            RecordsByPath[botexplode].ReplaceContents(ggpkPath, disable_botexplode, content.FreeRoot);
                            RecordsByPath[cyl].ReplaceContents(ggpkPath, disable_cyl, content.FreeRoot);
                            RecordsByPath[line].ReplaceContents(ggpkPath, disable_line, content.FreeRoot);
                            RecordsByPath[ice_botexplode].ReplaceContents(ggpkPath, disable_ice_botexplode, content.FreeRoot);
                            RecordsByPath[ice_botmist].ReplaceContents(ggpkPath, disable_ice_botmist, content.FreeRoot);
                            RecordsByPath[ice_line].ReplaceContents(ggpkPath, disable_ice_line, content.FreeRoot);
                            RecordsByPath[ice_lineactual].ReplaceContents(ggpkPath, disable_ice_lineactual, content.FreeRoot);
                            RecordsByPath[light_botexplode].ReplaceContents(ggpkPath, disable_light_botexplode, content.FreeRoot);
                            RecordsByPath[light_cyl].ReplaceContents(ggpkPath, disable_light_cyl, content.FreeRoot);
                            RecordsByPath[light_light_linger].ReplaceContents(ggpkPath, disable_light_light_linger, content.FreeRoot);
                            RecordsByPath[light_line].ReplaceContents(ggpkPath, disable_light_line, content.FreeRoot);
                            RecordsByPath[fire_discharge].ReplaceContents(ggpkPath, disable_fire_discharge, content.FreeRoot);
                            RecordsByPath[fire_discharge_NoSound].ReplaceContents(ggpkPath, disable_fire_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[light_discharge].ReplaceContents(ggpkPath, disable_light_discharge, content.FreeRoot);
                            RecordsByPath[light_discharge_NoSound].ReplaceContents(ggpkPath, disable_light_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[ice_discharge].ReplaceContents(ggpkPath, disable_ice_discharge, content.FreeRoot);
                            RecordsByPath[ice_discharge_NoSound].ReplaceContents(ggpkPath, disable_ice_discharge_NoSound, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            const string enable_fire_actual_line = "config/skillEffects/dischargeDisabled/fire/enable_actual_line.pet";
                            const string enable_fire_botexplode = "config/skillEffects/dischargeDisabled/fire/enable_botexplode.pet";
                            const string enable_fire_cyl = "config/skillEffects/dischargeDisabled/fire/enable_cyl.pet";
                            const string enable_fire_line = "config/skillEffects/dischargeDisabled/fire/enable_line.pet";
                            const string enable_ice_botexplode = "config/skillEffects/dischargeDisabled/ice/enable_botexplode.pet";
                            const string enable_ice_botmist = "config/skillEffects/dischargeDisabled/ice/enable_botmist.pet";
                            const string enable_ice_line = "config/skillEffects/dischargeDisabled/ice/enable_line.pet";
                            const string enable_ice_lineactual = "config/skillEffects/dischargeDisabled/ice/enable_lineactual.pet";
                            const string enable_light_botexplode = "config/skillEffects/dischargeDisabled/light/enable_botexplode.pet";
                            const string enable_light_cyl = "config/skillEffects/dischargeDisabled/light/enable_cyl.pet";
                            const string enable_light_light_linger = "config/skillEffects/dischargeDisabled/light/enable_light_linger.pet";
                            const string enable_light_line = "config/skillEffects/dischargeDisabled/light/enable_line.pet";
                            const string enable_fire_discharge = "config/skillEffects/dischargeDisabled/enable_fire_discharge.aoc";
                            const string enable_fire_discharge_NoSound = "config/skillEffects/dischargeDisabled/enable_fire_discharge_NoSound.aoc";
                            const string enable_light_discharge = "config/skillEffects/dischargeDisabled/enable_light_discharge.aoc";
                            const string enable_light_discharge_NoSound = "config/skillEffects/dischargeDisabled/enable_light_discharge_NoSound.aoc";
                            const string enable_ice_discharge = "config/skillEffects/dischargeDisabled/enable_ice_discharge.aoc";
                            const string enable_ice_discharge_NoSound = "config/skillEffects/dischargeDisabled/enable_ice_discharge_NoSound.aoc";

                            const string fire_actual_line = "Metadata\\Particles\\discharge\\fire\\actual_line.pet";
                            const string fire_botexplode = "Metadata\\Particles\\discharge\\fire\\botexplode.pet";
                            const string fire_cyl = "Metadata\\Particles\\discharge\\fire\\cyl.pet";
                            const string fire_line = "Metadata\\Particles\\discharge\\fire\\line.pet";
                            const string ice_botexplode = "Metadata\\Particles\\discharge\\ice\\botexplode.pet";
                            const string ice_botmist = "Metadata\\Particles\\discharge\\ice\\botmist.pet";
                            const string ice_line = "Metadata\\Particles\\discharge\\ice\\line.pet";
                            const string ice_lineactual = "Metadata\\Particles\\discharge\\ice\\lineactual.pet";
                            const string light_botexplode = "Metadata\\Particles\\discharge\\light\\botexplode.pet";
                            const string light_cyl = "Metadata\\Particles\\discharge\\light\\cyl.pet";
                            const string light_light_linger = "Metadata\\Particles\\discharge\\light\\light_linger.pet";
                            const string light_line = "Metadata\\Particles\\discharge\\light\\line.pet";
                            const string fire_discharge = "Metadata\\Effects\\Spells\\discharge\\fire_discharge.aoc";
                            const string fire_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\fire_discharge_NoSound.aoc";
                            const string light_discharge = "Metadata\\Effects\\Spells\\discharge\\light_discharge.aoc";
                            const string light_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\light_discharge_NoSound.aoc";
                            const string ice_discharge = "Metadata\\Effects\\Spells\\discharge\\ice_discharge.aoc";
                            const string ice_discharge_NoSound = "Metadata\\Effects\\Spells\\discharge\\ice_discharge_NoSound.aoc";

                            RecordsByPath[fire_actual_line].ReplaceContents(ggpkPath, enable_fire_actual_line, content.FreeRoot);
                            RecordsByPath[fire_botexplode].ReplaceContents(ggpkPath, enable_fire_botexplode, content.FreeRoot);
                            RecordsByPath[fire_cyl].ReplaceContents(ggpkPath, enable_fire_cyl, content.FreeRoot);
                            RecordsByPath[fire_line].ReplaceContents(ggpkPath, enable_fire_line, content.FreeRoot);
                            RecordsByPath[ice_botexplode].ReplaceContents(ggpkPath, enable_ice_botexplode, content.FreeRoot);
                            RecordsByPath[ice_botmist].ReplaceContents(ggpkPath, enable_ice_botmist, content.FreeRoot);
                            RecordsByPath[ice_line].ReplaceContents(ggpkPath, enable_ice_line, content.FreeRoot);
                            RecordsByPath[ice_lineactual].ReplaceContents(ggpkPath, enable_ice_lineactual, content.FreeRoot);
                            RecordsByPath[light_botexplode].ReplaceContents(ggpkPath, enable_light_botexplode, content.FreeRoot);
                            RecordsByPath[light_cyl].ReplaceContents(ggpkPath, enable_light_cyl, content.FreeRoot);
                            RecordsByPath[light_light_linger].ReplaceContents(ggpkPath, enable_light_light_linger, content.FreeRoot);
                            RecordsByPath[light_line].ReplaceContents(ggpkPath, enable_light_line, content.FreeRoot);
                            RecordsByPath[fire_discharge].ReplaceContents(ggpkPath, enable_fire_discharge, content.FreeRoot);
                            RecordsByPath[fire_discharge_NoSound].ReplaceContents(ggpkPath, enable_fire_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[light_discharge].ReplaceContents(ggpkPath, enable_light_discharge, content.FreeRoot);
                            RecordsByPath[light_discharge_NoSound].ReplaceContents(ggpkPath, enable_light_discharge_NoSound, content.FreeRoot);
                            RecordsByPath[ice_discharge].ReplaceContents(ggpkPath, enable_ice_discharge, content.FreeRoot);
                            RecordsByPath[ice_discharge_NoSound].ReplaceContents(ggpkPath, enable_ice_discharge_NoSound, content.FreeRoot);

                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void treeView1_MouseDoubleClick_1(object sender, MouseButtonEventArgs e)
        {
            TreeView source = sender as TreeView;

            Point hitPoint = e.GetPosition(source);
            if (source != null)
            {
                DependencyObject hitElement = (DependencyObject)source.InputHitTest(hitPoint);
                while (hitElement != null && !(hitElement is TreeViewItem))
                {
                    hitElement = VisualTreeHelper.GetParent(hitElement);
                }
            }
        }

        private void Window_PreviewDrop_1(object sender, DragEventArgs e)
        {
            if (!content.IsReadOnly)
            {
                e.Effects = DragDropEffects.Link;
            }
        }

        private void Window_Drop_1(object sender, DragEventArgs e)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            // Bring-to-front hack
            Topmost = true;
            Topmost = false;

            // reset viewer to show output message
            ResetViewer();
            textBoxOutput.Text = string.Empty;
            textBoxOutput.Visibility = Visibility.Visible;

            if (MessageBox.Show(Settings.Strings["MainWindow_Window_Drop_Confirm"], Settings.Strings["MainWindow_Window_Drop_Confirm_Caption"], MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            string[] fileNames = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (fileNames == null || fileNames.Length != 1)
            {
                OutputLine(Settings.Strings["MainWindow_Drop_Failed"]);
                return;
            }

            if (Directory.Exists(fileNames[0]))
            {
                HandleDropDirectory(fileNames[0]);
            }
            else if (string.Compare(Path.GetExtension(fileNames[0]), ".zip", StringComparison.OrdinalIgnoreCase) == 0)
            {
                // Zip file
                HandleDropArchive(fileNames[0]);
            }
            else
            {
                HandleDropFile(fileNames[0]);
            }
        }

        private void Window_Closing_1(object sender, System.ComponentModel.CancelEventArgs e)
        {
            workerThread?.Abort();
        }
    }
}