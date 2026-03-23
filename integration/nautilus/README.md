# Nautilus File Manager Integration

This directory contains the Nautilus (GNOME Files) integration for gc (Git Copy), enabling right-click context menu integration for generating AI-ready markdown from directories.

## Features

- **Right-click integration**: Simply right-click any directory in Nautilus
- **Clipboard output**: Automatically copies generated markdown to clipboard
- **Progress notifications**: Shows desktop notifications during processing
- **Error handling**: Graceful error messages and notifications

## Installation

### Prerequisites

- Nautilus file manager (GNOME Files)
- gc command-line tool installed and in PATH
- Linux system with XDG desktop notifications

### Quick Install

```bash
cd integration/nautilus
chmod +x setup.sh
./setup.sh
```

The installer will:
1. Check your system compatibility
2. Create the Nautilus scripts directory
3. Install the integration script
4. Restart Nautilus to pick up changes

## Usage

1. Open Nautilus file manager
2. Right-click on any directory
3. Navigate to: **Scripts → gc-nautilus**
4. Click to run gc on the selected directory

The script will:
- Generate AI-ready markdown from the directory
- Copy it to your clipboard
- Show notifications for progress and completion

## How It Works

The integration script (`gc-nautilus.sh`) is called by Nautilus when you select it from the context menu. Nautilus passes the selected directory path as an argument, and the script:

1. Validates that gc is installed
2. Changes to the selected directory
3. Runs `gc` to generate markdown
4. Copies the output to clipboard
5. Shows desktop notifications

## Files

- **gc-nautilus.sh**: Main integration script (installed to `~/.local/share/nautilus/scripts/`)
- **setup.sh**: Installation script that sets up the integration
- **test-integration.sh**: Test script to verify the integration works

## Troubleshooting

### Script doesn't appear in context menu

**Solution**: Restart Nautilus:
```bash
nautilus -q
nautilus &
```

### Script appears but doesn't work

**Check 1**: Verify gc is installed:
```bash
which gc
```

**Check 2**: Test the script manually:
```bash
~/.local/share/nautilus/scripts/gc-nautilus.sh /path/to/directory
```

**Check 3**: Check the script is executable:
```bash
ls -l ~/.local/share/nautilus/scripts/gc-nautilus.sh
```

### No notifications appear

Make sure you have a notification daemon running:
```bash
# Check for notification daemon
ps aux | grep notify
```

Most GNOME systems have this by default. If not, install:
```bash
sudo apt install notification-daemon
```

## Uninstall

Remove the integration script:
```bash
rm ~/.local/share/nautilus/scripts/gc-nautilus.sh
```

Then restart Nautilus:
```bash
nautilus -q
```

## Development

### Testing the Integration

Run the test script:
```bash
chmod +x test-integration.sh
./test-integration.sh
```

### Manual Testing

Test the script directly:
```bash
./gc-nautilus.sh /path/to/test/directory
```

### Editing the Script

If you modify `gc-nautilus.sh`, you need to:
1. Re-run `./setup.sh` to reinstall it
2. Restart Nautilus (`nautilus -q`)

## Supported Desktop Environments

- **GNOME**: Fully supported (primary target)
- **Unity**: Fully supported
- **Cinnamon**: Should work (uses Nautilus)
- **MATE**: Should work (uses Caja/Nemo)
- **KDE**: Not supported (different file manager, Dolphin)

For KDE, you would need to create a Dolphin service menu instead.

## License

Same license as the main gc project.

## Contributing

If you find issues or have improvements, please:
1. Test thoroughly with the `test-integration.sh` script
2. Open an issue on GitHub with:
   - Your desktop environment (GNOME version, etc.)
   - Linux distribution
   - Steps to reproduce
   - Error messages

## Related

- Main gc project: https://github.com/yourusername/gc
- gc documentation: See main project README
