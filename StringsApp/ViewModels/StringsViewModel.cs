using System;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia.Threading;

namespace StringsApp.ViewModels;

public partial class StringsViewModel : ViewModelBase
{
    public int MinLen => 4;

    const int BlockSize = 4 << 20; // 4 MiB

    private ObservableCollection<StringResult> _strings;
    public FlatTreeDataGridSource<StringResult> StringsSource { get; }

    public StringsViewModel()
    {
        _strings = new() { };

        StringsSource = new FlatTreeDataGridSource<StringResult>(_strings)
        {
            Columns =
            {
                new TextColumn<StringResult, long>("Position", x => x.Position),
                new TextColumn<StringResult, string>("String", x => x.Content)
            }
        };
    }

    public void Run(Stream file, CancellationToken ct, Action<double>? progressCallback)
    {
        byte[] buff = new byte[BlockSize];

        StringBuilder currentString = new();
        long startPos = 0;

        List<StringResult> foundStrs = [];
        long fileSize = file.Length;

        int numRead;
        var totalRead = 0;
        // TODO: Consider memory mapped files instead https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files
        // TODO: Parallelize, this is CPU-bound (especially once mmap implemented)
        while ((numRead = file.Read(buff, 0, BlockSize)) > 0)
        {
            if (ct.IsCancellationRequested) return;
            
            // Emit progress every block
            // This works for large blocks, but change to a modulus if block size is ever severely decreased 
            progressCallback?.Invoke((double)totalRead / fileSize);
            
            totalRead += numRead;
            for (var i = 0; i < numRead; i++)
            {
                byte c = buff[i];
                if ((c >= 0x20 && c <= 0x7e) || c == 0x09 /*|| c == 0x0a*/)
                {
                    if (currentString.Length == 0)
                    {
                        startPos = file.Position - BlockSize + i;
                    }

                    currentString.Append((char)c);
                }
                else
                {
                    if (currentString.Length >= MinLen)
                    {
                        foundStrs.Add(new StringResult(currentString.ToString(), startPos));
                    }

                    currentString.Clear();
                }
            }
        }

        if (currentString.Length >= MinLen)
        {
            foundStrs.Add(new(currentString.ToString(), startPos));
        }

        _strings = new(foundStrs);
        Dispatcher.UIThread.Invoke(() => { StringsSource.Items = _strings; });

        Debug.WriteLine(foundStrs.Count);
    }
}