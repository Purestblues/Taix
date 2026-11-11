using System;
using ShiJian.Client.Foundation;
using ShiJian.Client.Foundation.Rx;
using ShiJian.Client.Models;

namespace ShiJian.Client.ViewModels;

public class ViewModelBase : UINotifyPropertyChanged, IDisposable
{
    protected readonly CompositeDisposable Disposables = new();

    public virtual void Dispose()
    {
        Disposables.Dispose();
    }
}
