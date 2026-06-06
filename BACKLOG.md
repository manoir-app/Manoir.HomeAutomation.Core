# Backlog

## Technical Debt

- Replace hardcoded absolute base URL generation in Device contracts logic, currently in `DeviceActionnable.ToUrl`, once MaNoir.Core exposes a shared way to resolve the main/public base URL. Keep the current hardcoded behavior until that Core capability exists to avoid inventing a local workaround.