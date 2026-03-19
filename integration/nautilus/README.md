# gc - Nautilus Integration

This directory contains scripts to integrate `gc` (Git Copy) with the Nautilus file explorer (GNOME). This allows you to right-click on folders or files and copy their contents as markdown directly to your clipboard.

## 🚀 Quick Setup

Run the setup script to automatically integrate `gc` with Nautilus:

```bash
chmod +x integration/nautilus/setup.sh
./integration/nautilus/setup.sh
```

## 🛠 Manual Installation

If you prefer manual setup:

1.  Ensure `gc` is installed and in your `PATH` (run `gc --help` in terminal to check).
2.  Make the integration script executable:
    ```bash
    chmod +x integration/nautilus/gc-nautilus.sh
    ```
3.  Create a symbolic link to the Nautilus scripts folder:
    ```bash
    mkdir -p ~/.local/share/nautilus/scripts
    ln -s "$(realpath integration/nautilus/gc-nautilus.sh)" ~/.local/share/nautilus/scripts/gc
    ```
4.  (Optional) Restart Nautilus by running `nautilus -q` in your terminal.

## 📝 Usage

1.  Open Nautilus.
2.  Right-click on any folder or file (or multiple selections).
3.  Go to **Scripts** -> **gc**.
4.  You will see a notification once the copy is complete.

## 📋 Requirements

- `gc`: The core Git Copy tool.
- `libnotify-bin`: (Optional) Provides `notify-send` for visual feedback.
- `xclip` or `wl-clipboard`: Required for clipboard support on Linux.

## 🔍 Troubleshooting

If it doesn't work:
- Check the log file: `/tmp/gc-nautilus.log`.
- Ensure `gc` is in your `PATH`.
- Make sure the script is executable (`chmod +x`).
