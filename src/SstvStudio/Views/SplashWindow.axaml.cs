// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using Avalonia.Controls;
using SstvStudio.Services;

namespace SstvStudio.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version}  ·  {AppInfo.Copyright}";
    }

    public string Status
    {
        set => StatusText.Text = value;
    }
}
