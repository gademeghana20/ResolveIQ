import { test } from '@playwright/test';

import { ADMIN, EMPLOYEE, expectAccessible, signIn } from './helpers';

// Every main screen is checked with the axe engine against WCAG 2.1 A and AA.
// This finds what a machine can find (colour contrast, missing names and labels, bad structure); it does not replace
// trying the app with a keyboard and a screen reader.

test.describe('accessibility', () => {
  test('sign-in page', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('button', { name: 'Sign in' }).waitFor();
    await expectAccessible(page, 'the sign-in page');
  });

  test('create-an-account page', async ({ page }) => {
    await page.goto('/register');
    await page.getByLabel('Email').waitFor();
    await expectAccessible(page, 'the register page');
  });

  test('the screens of a signed-in employee', async ({ page }) => {
    await signIn(page, EMPLOYEE);

    await page.getByRole('heading', { name: 'Incidents' }).waitFor();
    await expectAccessible(page, 'the incident list');

    await page.goto('/dashboard');
    await page.getByRole('heading', { name: 'Dashboard' }).waitFor();
    await page.waitForLoadState('networkidle');
    await expectAccessible(page, 'the dashboard');

    await page.goto('/incidents/new');
    await page.getByLabel('Title').waitFor();
    await expectAccessible(page, 'the new-incident form');

    await page.goto('/account/password');
    await page.getByRole('heading', { name: /password/i }).first().waitFor();
    await expectAccessible(page, 'the change-password page');

    await page.goto('/nowhere');
    await page.getByRole('heading', { name: 'Page not found' }).waitFor();
    await expectAccessible(page, 'the page-not-found page');
  });

  test('the Admin screens', async ({ page }) => {
    await signIn(page, ADMIN);

    for (const [path, heading] of [
      ['/admin/users', 'Users'],
      ['/admin/categories', 'Categories'],
      ['/admin/sla', 'SLA policies'],
      ['/admin/audit', 'Audit log'],
    ] as const) {
      await page.goto(path);
      await page.getByRole('heading', { name: heading }).waitFor();
      await page.waitForLoadState('networkidle');
      await expectAccessible(page, `the Admin page ${heading}`);
    }
  });
});
