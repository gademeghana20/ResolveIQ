import AxeBuilder from '@axe-core/playwright';
import { expect, Page } from '@playwright/test';

import { DEMO_PASSWORD } from '../playwright.config';

export const ADMIN = 'admin@example.com';
export const MANAGER = 'manager@example.com';
export const EMPLOYEE = 'employee@example.com';

/** Signs in through the real login page. */
export async function signIn(page: Page, email: string, password = DEMO_PASSWORD): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();
}

export async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Log out' }).click();
  await expect(page).toHaveURL(/\/login/);
}

/** Picks an option in a Material dropdown by its visible label. */
export async function choose(page: Page, field: string, option: string | RegExp): Promise<void> {
  await page.getByRole('combobox', { name: field }).click();
  await page.getByRole('option', { name: option }).click();
}

/**
 * Checks the page against the accessibility rules of WCAG 2.1 A and AA, using the axe engine.
 * Any violation fails the test, and the message names the rule and the elements.
 */
export async function expectAccessible(page: Page, label: string): Promise<void> {
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();

  const summary = result.violations.map(
    (v) => `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.map((n) => n.target.join(' ')).join('\n  ')}`,
  );
  expect(summary, `Accessibility problems on ${label}`).toEqual([]);
}

/** A title no earlier run can have used, so tests can find "their" incident in a shared database. */
export function uniqueTitle(prefix: string): string {
  return `${prefix} ${Date.now().toString(36)}`;
}
