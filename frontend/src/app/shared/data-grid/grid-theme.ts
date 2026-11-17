import { themeQuartz } from 'ag-grid-community';

/**
 * How every table looks. The colours are not fixed values: they point at the app's own colour variables
 * (the same ones the buttons and cards use), so the tables follow the green brand palette and switch to
 * dark mode by themselves when the person flips the Light / Dark switch.
 */
export const resolveIqGridTheme = themeQuartz.withParams({
  fontFamily: 'inherit',
  fontSize: 13,
  foregroundColor: 'var(--mat-sys-on-surface)',
  backgroundColor: 'var(--mat-sys-surface-container-lowest)',
  // The header uses the same teal green as the side panel, so every table matches the rest of the app.
  headerBackgroundColor: 'var(--app-sidebar-bg)',
  headerTextColor: 'var(--app-sidebar-fg)',
  headerFontWeight: 600,
  borderColor: 'var(--mat-sys-outline-variant)',
  // Every other row is a soft grey, so the eye can follow a row across the table.
  oddRowBackgroundColor: 'var(--mat-sys-surface-container)',
  rowHoverColor: 'var(--mat-sys-surface-container-high)',
  accentColor: 'var(--mat-sys-primary)',
  wrapperBorderRadius: 16,
  borderRadius: 10,
  spacing: 8,
  cellHorizontalPadding: 10,
  headerColumnResizeHandleColor: 'transparent',
  columnBorder: false,
});
