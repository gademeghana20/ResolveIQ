// Takes the screenshots used in the README (docs/screenshots), by driving the real web app like a person.
//
// It needs the API and the web app already running on a database that has the demo data (Seed:DemoData=true).
// Nothing is changed in the app by the screenshots themselves: dialogs are opened and then cancelled.
//
//   SHOTS_PASSWORD   the shared demo password (required)
//   SHOTS_BASE       the web app           (default http://localhost:4200)
//   SHOTS_API        the API it should use (default http://localhost:5293)
//   E2E_BROWSER      "msedge" to use Edge instead of the downloaded Chromium
const { chromium } = require('@playwright/test');
const fs = require('node:fs');
const path = require('node:path');

const BASE = process.env.SHOTS_BASE || 'http://localhost:4200';
const API = process.env.SHOTS_API || 'http://localhost:5293';
const PASSWORD = process.env.SHOTS_PASSWORD;
const OUT = path.resolve(__dirname, '..', '..', 'docs', 'screenshots');

if (!PASSWORD) {
  console.error('Set SHOTS_PASSWORD to the shared demo password first.');
  process.exit(1);
}
fs.mkdirSync(OUT, { recursive: true });

async function open(browser, { dark = false, mobile = false } = {}) {
  const context = await browser.newContext({
    viewport: mobile ? { width: 390, height: 844 } : { width: 1440, height: 900 },
    colorScheme: dark ? 'dark' : 'light',
    deviceScaleFactor: 1,
  });
  // The web app reads where its API is from /config.js. For screenshots it is pointed at the local API.
  await context.route('**/config.js', (route) =>
    route.fulfill({
      contentType: 'application/javascript',
      body: `window.resolveiqConfig = { apiBaseUrl: ${JSON.stringify(API)}, demoPassword: ${JSON.stringify(PASSWORD)} };`,
    }),
  );
  return { context, page: await context.newPage() };
}

async function signInAs(page, label) {
  await page.goto(`${BASE}/login`);
  await page.getByRole('button', { name: label, exact: true }).click();
  await page.getByRole('button', { name: 'Log out' }).waitFor({ timeout: 30000 });
}

async function gridReady(page) {
  await page.locator('.ag-row').first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(700);
}

async function shot(page, name, { fullPage = false } = {}) {
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(OUT, `${name}.png`), fullPage });
  console.log('saved', name);
}

async function step(name, fn) {
  try {
    await fn();
  } catch (error) {
    console.error(`FAILED ${name}: ${String(error.message).split('\n')[0]}`);
  }
}

async function dashboardReady(page) {
  await page.goto(`${BASE}/dashboard`);
  await page.getByRole('heading', { name: 'Dashboard' }).waitFor();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000); // let the charts finish drawing
}

(async () => {
  const browser = await chromium.launch({ channel: process.env.E2E_BROWSER || undefined });

  // ---- Sign-in page
  await step('sign-in', async () => {
    const { context, page } = await open(browser);
    await page.goto(`${BASE}/login`);
    await page.getByRole('button', { name: 'Manager', exact: true }).waitFor();
    await shot(page, '01-sign-in');
    await context.close();
  });

  // ---- Employee
  {
    const { context, page } = await open(browser);
    await signInAs(page, 'Employee');

    await step('employee list', async () => {
      await page.getByRole('heading', { name: 'Incidents' }).waitFor();
      await gridReady(page);
      await shot(page, '02-employee-incidents');
    });

    await step('employee dashboard', async () => {
      await dashboardReady(page);
      await shot(page, '03-employee-dashboard', { fullPage: true });
    });

    await step('employee notifications', async () => {
      await page.goto(`${BASE}/incidents`);
      await gridReady(page);
      await page.getByRole('button', { name: /Notifications/ }).click();
      await page.waitForTimeout(1200);
      await shot(page, '04-employee-notifications');
      await page.keyboard.press('Escape');
    });

    await step('employee confirm the fix', async () => {
      await page.goto(`${BASE}/incidents`);
      await gridReady(page);
      await page.getByRole('link', { name: /Forgot my password/ }).click();
      await page.getByText('Is it fixed?').waitFor();
      await shot(page, '05-employee-confirm-the-fix', { fullPage: true });
    });

    await step('employee reports an incident', async () => {
      await page.goto(`${BASE}/incidents/new`);
      await page.getByLabel('Title').fill('Laptop fan is very loud');
      await page.getByLabel('Description').fill('The fan runs at full speed even when I only have a browser open. It started this week.');
      await page.getByRole('combobox', { name: /How urgent is this for you/ }).click();
      await page.getByRole('option', { name: 'Medium' }).click();
      await shot(page, '06-employee-report-incident');
    });

    await context.close();
  }

  // ---- Manager
  {
    const { context, page } = await open(browser);
    await signInAs(page, 'Manager');

    await step('manager list', async () => {
      await page.getByRole('heading', { name: 'Incidents' }).waitFor();
      await gridReady(page);
      await shot(page, '07-manager-incidents');
    });

    await step('manager dashboard', async () => {
      await dashboardReady(page);
      await shot(page, '08-manager-dashboard', { fullPage: true });
    });

    await step('manager overdue incident', async () => {
      await page.goto(`${BASE}/incidents`);
      await gridReady(page);
      await page.getByRole('link', { name: /Customer database is very slow/ }).click();
      await page.getByRole('heading', { name: /Customer database is very slow/ }).waitFor();
      await page.waitForTimeout(800);
      await shot(page, '09-manager-incident-detail', { fullPage: true });
    });

    await step('manager assign dialog', async () => {
      await page.goto(`${BASE}/incidents`);
      await gridReady(page);
      await page.getByRole('link', { name: /Cannot connect to the office Wi-Fi/ }).click();
      await page.getByRole('button', { name: /^Assign/ }).click();
      await page.getByRole('dialog').waitFor();
      await shot(page, '10-manager-assign-dialog');
      await page.keyboard.press('Escape');
    });

    await context.close();
  }

  // ---- Admin
  {
    const { context, page } = await open(browser);
    await signInAs(page, 'Admin');

    await step('admin users', async () => {
      await page.goto(`${BASE}/admin/users`);
      await page.getByRole('heading', { name: 'Users' }).waitFor();
      await gridReady(page);
      await shot(page, '11-admin-users');
    });

    await step('admin deactivate dialog', async () => {
      // The grid draws each person as a ".ag-row"; the "⋮" button at its end opens the row's menu.
      const row = page.locator('.ag-row', { hasText: 'alex.kim@example.com' });
      await row.locator('button').last().click();
      await page.locator('.cdk-overlay-container button', { hasText: 'Deactivate…' }).click();
      await page.getByRole('dialog').waitFor();
      await shot(page, '12-admin-deactivate-with-handover');
      await page.keyboard.press('Escape');
    });

    await step('admin categories', async () => {
      await page.goto(`${BASE}/admin/categories`);
      await page.getByRole('heading', { name: 'Categories' }).waitFor();
      await gridReady(page);
      await shot(page, '13-admin-categories');
    });

    await step('admin sla', async () => {
      await page.goto(`${BASE}/admin/sla`);
      await page.getByRole('heading', { name: 'SLA policies' }).waitFor();
      await gridReady(page);
      await shot(page, '14-admin-sla-policies');
    });

    await step('admin audit', async () => {
      await page.goto(`${BASE}/admin/audit`);
      await page.getByRole('heading', { name: 'Audit log' }).waitFor();
      await gridReady(page);
      await shot(page, '15-admin-audit-log');
    });

    await context.close();
  }

  // ---- Dark mode and a phone-sized screen
  await step('dark mode', async () => {
    const { context, page } = await open(browser, { dark: true });
    await signInAs(page, 'Manager');
    await dashboardReady(page);
    await shot(page, '16-dark-mode-dashboard', { fullPage: true });
    await context.close();
  });

  await step('phone', async () => {
    const { context, page } = await open(browser, { mobile: true });
    await page.goto(`${BASE}/login`);
    await page.getByRole('button', { name: 'Manager', exact: true }).click();
    await page.waitForURL(/\/incidents/, { timeout: 30000 });
    await page.locator('.ag-row').first().waitFor({ timeout: 30000 });
    await shot(page, '17-phone-incidents');
    await context.close();
  });

  await browser.close();
  console.log('done');
})();
