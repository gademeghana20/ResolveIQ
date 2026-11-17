import { themeQuartz } from 'ag-grid-community';

/**
 * How every table looks. The colours are not fixed values: they point at the app's own colour variables
 * (the same ones the buttons and cards use), so the tables follow the green brand palette and switch to
 * dark mode by themselves when the person flips the Light / Dark switch.
 */
export const resolveIqGridTheme = themeQuartz.withParams({
  fontFamily: 'inherit',
  fontSize: 14,
  foregroundColor: 'var(--mat-sys-on-surface)',
  backgroundColor: 'var(--mat-sys-surface-container-lowest)',
  headerBackgroundColor: 'var(--mat-sys-surface-container)',
  headerTextColor: 'var(--mat-sys-on-surface-variant)',
  headerFontWeight: 600,
  borderColor: 'var(--mat-sys-outline-variant)',
  rowHoverColor: 'var(--mat-sys-surface-container-low)',
  accentColor: 'var(--mat-sys-primary)',
  wrapperBorderRadius: 16,
  borderRadius: 10,
  spacing: 8,
  cellHorizontalPadding: 12,
  headerColumnResizeHandleColor: 'transparent',
  columnBorder: false,
});
