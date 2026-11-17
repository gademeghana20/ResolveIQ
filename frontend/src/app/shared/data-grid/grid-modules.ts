import { AllCommunityModule, ModuleRegistry } from 'ag-grid-community';

let registered = false;

/**
 * Tells AG Grid which of its features may be used (AG Grid ships its features as separate modules).
 * This file is only loaded together with a table, so people who never open a table never download any of it.
 */
export function registerGridModules(): void {
  if (!registered) {
    ModuleRegistry.registerModules([AllCommunityModule]);
    registered = true;
  }
}
