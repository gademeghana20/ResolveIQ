import fs from 'node:fs';

import { expect, test } from '@playwright/test';

import { DEMO_PASSWORD } from '../playwright.config';
import { ADMIN, EMPLOYEE, MANAGER, choose, expectAccessible, signIn, signOut, uniqueTitle } from './helpers';

test.describe('signing in', () => {
  test('a visitor is sent to the sign-in page, and a wrong password is refused', async ({ page }) => {
    await page.goto('/incidents');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fincidents/);

    await page.getByLabel('Email').fill(EMPLOYEE);
    await page.getByLabel('Password').fill('not-the-password');
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('alert')).toContainText('Invalid email or password.');
  });

  test('the right password opens the page the person first asked for', async ({ page }) => {
    await page.goto('/incidents');
    await page.getByLabel('Email').fill(EMPLOYEE);
    await page.getByLabel('Password').fill(DEMO_PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/incidents$/);
    await expect(page.getByRole('heading', { name: 'Incidents' })).toBeVisible();
  });

  test('an address that does not exist shows a proper "page not found"', async ({ page }) => {
    await signIn(page, EMPLOYEE);
    await page.goto('/this/page/does/not/exist');

    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
    await page.getByRole('link', { name: 'Go to the dashboard', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
  });
});

test.describe('who sees what', () => {
  test('an employee has no Admin area, and is turned away from its address', async ({ page }) => {
    await signIn(page, EMPLOYEE);

    await expect(page.getByRole('link', { name: 'Admin' })).toHaveCount(0);
    await page.goto('/admin/users');
    await expect(page).not.toHaveURL(/\/admin/);
  });

  test('an admin has the Admin area, with the audit log', async ({ page }) => {
    await signIn(page, ADMIN);

    await page.getByRole('link', { name: 'Admin' }).click();
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
    await page.getByRole('link', { name: 'Audit log' }).click();
    await expect(page.getByRole('heading', { name: 'Audit log' })).toBeVisible();
  });
});

test.describe('an incident from report to close', () => {
  test('reported, assigned, worked, resolved with a note, and confirmed by the reporter', async ({ page }) => {
    const title = uniqueTitle('Printer on floor 2 jams');

    // 1. An employee reports it.
    await signIn(page, EMPLOYEE);
    await page.getByRole('link', { name: 'New incident' }).click();
    await page.getByLabel('Title').fill(title);
    await page.getByLabel('Description').fill('Paper jams every time we print double-sided.');
    await choose(page, /How urgent is this for you/, 'High');
    await page.getByRole('button', { name: /Report|Submit|Create/ }).click();

    await expect(page.getByRole('heading', { name: title })).toBeVisible();
    await expect(page.getByText('Reported urgency')).toBeVisible();
    await signOut(page);

    // 2. A manager assigns it and sets the priority.
    await signIn(page, MANAGER);
    await page.goto('/incidents');
    await page.getByRole('link', { name: title }).click();
    await page.getByRole('button', { name: /^Assign/ }).click();
    await choose(page, 'Assign to', ADMIN);
    await choose(page, 'Priority', 'High');
    await page.getByRole('dialog').getByRole('button', { name: 'Assign', exact: true }).click();
    await expect(page.getByText('SLA: On track')).toBeVisible();
    await expect(page.getByText('Time left')).toBeVisible(); // the clock is running
    await signOut(page);

    // 3. The assignee starts work and resolves it, saying how it was fixed.
    await signIn(page, ADMIN);
    await page.goto('/incidents');
    await page.getByRole('link', { name: title }).click();
    await page.getByRole('button', { name: 'Start work' }).click();
    await expect(page.getByText('In progress').first()).toBeVisible();
    await page.getByRole('button', { name: 'Mark resolved…' }).click();
    await page.getByRole('dialog').getByRole('textbox').fill('Replaced the paper tray roller.');
    await page.getByRole('dialog').getByRole('button', { name: 'Mark resolved' }).click();
    await expect(page.getByText('How it was fixed')).toBeVisible();
    await signOut(page);

    // 4. The reporter is told, reads the answer and confirms.
    await signIn(page, EMPLOYEE);
    await page.getByRole('button', { name: /Notifications/ }).click();
    await expect(page.getByText(/was resolved by/).first()).toBeVisible();
    await page.keyboard.press('Escape');
    await page.goto('/incidents');
    await page.getByRole('link', { name: title }).click();
    await expect(page.getByText('Is it fixed?')).toBeVisible();
    await expect(page.getByText('Replaced the paper tray roller.', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Close incident…' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Close incident' }).click();
    await expect(page.getByText('Closed').first()).toBeVisible();
  });
});

test.describe('leaving a half-filled form', () => {
  test('asks first, and stays put when the person cancels', async ({ page }) => {
    await signIn(page, EMPLOYEE);
    await page.getByRole('link', { name: 'New incident' }).click();
    await page.getByLabel('Title').fill('Half written');

    await page.getByRole('link', { name: 'Dashboard', exact: true }).click();
    const question = page.getByRole('dialog');
    await expect(question).toContainText('Leave this page?');
    await question.getByRole('button', { name: 'Cancel' }).click();

    await expect(page).toHaveURL(/\/incidents\/new/);
    await expect(page.getByLabel('Title')).toHaveValue('Half written');
  });
});

test.describe('the Admin area', () => {
  test('a new category appears in the audit log', async ({ page }) => {
    const name = uniqueTitle('Facilities');

    await signIn(page, ADMIN);
    await page.goto('/admin/categories');
    await page.getByRole('button', { name: 'New category' }).click();
    await page.getByRole('dialog').getByLabel('Name').fill(name);
    await page.getByRole('dialog').getByRole('button', { name: /Create|Save/ }).click();
    await expect(page.locator('.ag-cell-value', { hasText: name })).toBeVisible();

    await page.goto('/admin/audit');
    await expect(page.getByRole('row', { name: new RegExp(name) })).toContainText('Category created');
  });

  test('the CSV export downloads a file with the incidents in it', async ({ page }) => {
    await signIn(page, MANAGER);
    await page.goto('/incidents');

    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Export CSV' }).click()]);

    expect(download.suggestedFilename()).toMatch(/^incidents-\d{4}-\d{2}-\d{2}\.csv$/);
  });
});

test.describe('files on an incident', () => {
  test('a file attached while reporting is there for the manager to download, and the page stays accessible', async ({ page }) => {
    const title = uniqueTitle('Error message attached');

    // 1. An employee reports it and attaches a note.
    await signIn(page, EMPLOYEE);
    await page.getByRole('link', { name: 'New incident' }).click();
    await page.getByLabel('Title').fill(title);
    await page.getByLabel('Description').fill('The notes file has the steps that cause it.');
    await page.locator('input[type="file"]').setInputFiles({
      name: 'steps.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('Steps to reproduce\n1. open the report\n2. it crashes\n'),
    });
    await expect(page.locator('.chosen')).toContainText('steps.txt');
    await page.getByRole('button', { name: 'Submit incident' }).click();

    await expect(page.getByRole('heading', { name: title })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Download steps.txt' })).toBeVisible();
    await expect(page.getByText('Attached the file "steps.txt"')).toBeVisible(); // it is in the history too
    await expectAccessible(page, 'an incident with an attached file');
    await signOut(page);

    // 2. A manager opens the incident and downloads the very same file.
    await signIn(page, MANAGER);
    await page.goto('/incidents');
    await page.getByRole('link', { name: title }).click();
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: 'Download steps.txt' }).click(),
    ]);

    expect(download.suggestedFilename()).toBe('steps.txt');
    expect(fs.readFileSync((await download.path())!, 'utf8')).toContain('Steps to reproduce');
  });

  test('a file the app does not allow is refused before anything is sent', async ({ page }) => {
    await signIn(page, EMPLOYEE);
    await page.getByRole('link', { name: 'New incident' }).click();

    await page.locator('input[type="file"]').setInputFiles({
      name: 'installer.exe',
      mimeType: 'application/octet-stream',
      buffer: Buffer.from('MZ not really a program'),
    });

    await expect(page.getByRole('alert')).toContainText('installer.exe');
    await expect(page.getByRole('alert')).toContainText('not allowed');
    await expect(page.locator('.chosen')).toHaveCount(0);
  });

  test('a file on a comment shows under that comment, and its author can remove it', async ({ page }) => {
    const title = uniqueTitle('Comment with a file');

    await signIn(page, EMPLOYEE);
    await page.getByRole('link', { name: 'New incident' }).click();
    await page.getByLabel('Title').fill(title);
    await page.getByLabel('Description').fill('Plain incident, the file comes with a comment.');
    await page.getByRole('button', { name: 'Submit incident' }).click();
    await expect(page.getByRole('heading', { name: title })).toBeVisible();

    await page.getByLabel('Add a comment').fill('Here is the log.');
    await page.locator('app-comment-form input[type="file"]').setInputFiles({
      name: 'router.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('log line 1\nlog line 2\n'),
    });
    await page.getByRole('button', { name: 'Post comment' }).click();

    const comment = page.locator('article.comment', { hasText: 'Here is the log.' });
    await expect(comment.getByRole('button', { name: 'Download router.txt' })).toBeVisible();

    await comment.getByRole('button', { name: 'Remove router.txt' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Remove' }).click();
    await expect(comment.getByRole('button', { name: 'Download router.txt' })).toHaveCount(0);
    await expect(page.getByText('Removed the file "router.txt"')).toBeVisible();
  });
});
