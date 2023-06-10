using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// File format.
    /// </summary>
    class FileFormat : IApplicationObject, INotifyPropertyChanged
    {
        public FileFormat(IApplication app, string id, IEnumerable<string> extensions)
        {
            this.Application = app;
            this.Extensions = new HashSet<string>(extensions).AsReadOnly();
            this.Id = id;
            this.Name = app.GetStringNonNull($"FileType.{id}", id);
            app.StringsUpdated += (_, _) =>
            {
                this.Name = app.GetStringNonNull($"FileType.{id}", id);
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            };
        }


        // Application.
        public IApplication Application { get; }


        // Check thread.
        public bool CheckAccess() => this.Application.CheckAccess();


        // Extensions.
        public ISet<string> Extensions { get; }


        // ID of format.
        public string Id { get; }


        // Name of format.
        public string Name { get; private set; }


        // Raised when property changed.
        public event PropertyChangedEventHandler? PropertyChanged;


        // SynchronizationContext
        public SynchronizationContext SynchronizationContext => this.Application.SynchronizationContext;


        /// <inheritdoc/>
        public override string ToString() => this.Id;
    }
}
