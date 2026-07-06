using System;
using System.ComponentModel;
using System.Windows;
using OxyPlot;

namespace MyRevitAddin.Views
{
    public partial class DiagramWindow : Window, INotifyPropertyChanged
    {
        public string WindowTitle { get; set; }

        public DiagramWindow()
        {
            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Instead of closing and destroying, we can just hide it so we don't have to manage recreations
            // Or we let it close and the caller manages instantiating a new one.
            // Based on prompt "manage non-modal windows", it's usually cleaner to let them close
            // and have the caller null out its reference.
            base.OnClosing(e);
        }
    }
}
