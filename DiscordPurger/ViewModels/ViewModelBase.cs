using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DiscordPurger.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}