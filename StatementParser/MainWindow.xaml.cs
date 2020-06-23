using CsvHelper;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Text;
using System.Linq;

namespace StatementParser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        internal static readonly DependencyPropertyKey IsParsingKey = DependencyProperty.RegisterReadOnly(
            "IsParsing",
            typeof(bool),
            typeof(MainWindow),
            new PropertyMetadata(false)
        );
        public static readonly DependencyProperty IsParsingProperty =
            IsParsingKey.DependencyProperty;
        public bool IsParsing
        {
            internal set { SetValue(IsParsingKey, value); }
            get { return (bool)GetValue(IsParsingProperty); }
        }

        public bool FolderMode
        {
            get { return (bool)GetValue(FolderModeProperty); }
            set { SetValue(FolderModeProperty, value); }
        }
        public static readonly DependencyProperty FolderModeProperty =
            DependencyProperty.Register("FolderMode", typeof(bool), typeof(MainWindow));

        public bool IncludeSubfolders
        {
            get { return (bool)GetValue(IncludeSubfoldersProperty); }
            set { SetValue(IncludeSubfoldersProperty, value); }
        }
        public static readonly DependencyProperty IncludeSubfoldersProperty =
            DependencyProperty.Register("IncludeSubfolders", typeof(bool), typeof(MainWindow));

        public string InputPath
        {
            get { return (string)GetValue(InputPathProperty); }
            set { SetValue(InputPathProperty, value); }
        }
        public static readonly DependencyProperty InputPathProperty =
            DependencyProperty.Register("InputPath", typeof(string), typeof(MainWindow));

        public string OutputPath
        {
            get { return (string)GetValue(OutputPathProperty); }
            set { SetValue(OutputPathProperty, value); }
        }
        public static readonly DependencyProperty OutputPathProperty =
            DependencyProperty.Register("OutputPath", typeof(string), typeof(MainWindow));

        private void BrowseForInput(object sender, RoutedEventArgs e)
        {
            if (FolderMode)
            {
                var dialog = new CommonOpenFileDialog() { IsFolderPicker = true, Title = "Select a folder containing PDF files"};
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    InputPath = dialog.FileName;
                }
            }
            else
            {
                var dialog = new CommonOpenFileDialog { Multiselect = true, Title = "Select one or more PDF files" };
                dialog.Filters.Add(new CommonFileDialogFilter("PDF File", "*.pdf"));
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    if (dialog.FileNames.Count() == 1)
                    {
                        InputPath = dialog.FileName;
                    }
                    else
                    {
                        var builder = new StringBuilder();

                        foreach (var file in dialog.FileNames)
                        {
                            if (builder.Length > 0) { builder.Append(' '); }

                            builder.Append('"');
                            builder.Append(file);
                            builder.Append('"');
                        }

                        InputPath = builder.ToString();
                    }
                }
            }
        }

        private void BrowseForOutput(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonSaveFileDialog() { Title = "Where do you want to save the output file", DefaultExtension = ".csv" };
            dialog.Filters.Add(new CommonFileDialogFilter("Comma seperated text file", "*.csv"));
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath = dialog.FileName;
            }
        }

        private void Start(object sender, RoutedEventArgs e)
        {
            IsParsing = true;

            string error = Validate(out var files);

            if (error != null)
            {
                MessageBox.Show(error);
                IsParsing = false;
                return;
            }

            var transactions = new List<Transaction>();

            foreach (string file in files)
            {
                var batch = Parser.ParseStatement(file);

                if (batch == null)
                {
                    if (files.Count() == 1)
                    {
                        MessageBox.Show($@"The file ""{file}"" could not be parsed. It was not recognized as a supported statement type.", "Unable to Parse Statement");
                        IsParsing = false;
                        return;
                    }
                    else
                    {
                        if (MessageBox.Show($@"The file ""{file}"" could not be parsed. It was not recognized as a supported statement type. Do you want to skip this statement and continue?", "Unable to Parse Statement", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        { continue; }
                        else
                        {
                            IsParsing = false;
                            return;
                        }
                    }                    
                }

                transactions.AddRange(batch);
            }

            SaveToCSV(transactions, OutputPath);

            IsParsing = false;

            MessageBox.Show("Parsing completed successfully");
        }

        private string Validate(out IEnumerable<string> files)
        {
            files = null;
            
            try
            {
                var outFile = new FileInfo(OutputPath);
            }
            catch
            {
                return "The output path is not a valid file path or is not accessable";
            }

            if (string.IsNullOrWhiteSpace(InputPath)) { return @"Input path cannot be empty"; }
            
            if (FolderMode)
            {
                if (!Directory.Exists(InputPath)) { return @"The input folder does not exist"; }

                files = Directory.EnumerateFiles(InputPath, "*.pdf", SearchOption.AllDirectories);
            }
            else
            {
                if (InputPath.Contains('"'))
                {
                    var fileList = new List<string>();

                    StringBuilder currentFile = null;

                    foreach(char c in InputPath)
                    {
                        if (c == '"')
                        {
                            if (currentFile == null)
                            {
                                currentFile = new StringBuilder();
                            }
                            else
                            {
                                fileList.Add(currentFile.ToString());
                                currentFile = null;
                            }
                        }
                        else
                        {
                            if (currentFile == null)
                            {
                                if (!char.IsWhiteSpace(c)) { return "Input path is not a valid file name or sequence of file names"; }
                            }
                            else
                            {
                                currentFile.Append(c);
                            }
                        }
                    }

                    files = fileList;
                }
                else
                {
                    if (!File.Exists(InputPath)) { return "Input file does not exist"; }

                    files = new string[] { InputPath };
                }
            }

            return null;
        }

        private void SaveToCSV(IList<Transaction> transactions, string outputFile)
        {
            using (var writer = new StreamWriter(outputFile))
            using (var csv = new CsvWriter(writer, CultureInfo.CurrentCulture))
            {
                csv.WriteRecords(transactions);
            }
        }
    }
}
