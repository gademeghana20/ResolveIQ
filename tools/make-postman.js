#!/usr/bin/env node
/**
 * Builds the Postman collection from the API's own OpenAPI description, so the collection always matches the real
 * endpoints instead of being typed by hand.
 *
 * How to run it (from the repository root):
 *   1. Start the API in Development:   dotnet run --project backend/ResolveIQ.API
 *   2. Save its description:           curl http://localhost:5293/openapi/v1.json -o openapi.json
 *   3. Build the collection:           node tools/make-postman.js openapi.json
 *
 * It writes docs/postman/ResolveIQ.postman_collection.json and docs/postman/ResolveIQ.local.postman_environment.json.
 * Nothing secret goes in either file: the environment's email and password start empty.
 */
const fs = require('fs');
const path = require('path');

const inputPath = process.argv[2];
if (!inputPath) {
  console.error('Usage: node tools/make-postman.js <openapi.json>');
  process.exit(1);
}

const spec = JSON.parse(fs.readFileSync(inputPath, 'utf8'));
const outDir = path.join(__dirname, '..', 'docs', 'postman');
fs.mkdirSync(outDir, { recursive: true });

// ---------------------------------------------------------------------------------------------------------------
// Friendly names, and which endpoints are open (no login needed)
// ---------------------------------------------------------------------------------------------------------------
const NAMES = {
  'POST /api/auth/register': 'Register (creates an Employee and signs in)',
  'POST /api/auth/login': 'Log in',
  'POST /api/auth/refresh-token': 'Renew the login (refresh token)',
  'POST /api/auth/logout': 'Log out (cancels the refresh token)',
  'POST /api/auth/change-password': 'Change my password',
  'POST /api/auth/forgot-password': 'Forgot password (sends an email)',
  'POST /api/auth/reset-password': 'Reset password (token from the email)',
  'GET /api/incidents': 'List incidents (search, filter, sort, page)',
  'POST /api/incidents': 'Create an incident',
  'GET /api/incidents/{id}': 'Get one incident',
  'PUT /api/incidents/{id}': 'Edit the title and description (reporter only)',
  'PUT /api/incidents/{id}/assign': 'Assign an incident (Manager or Admin)',
  'PUT /api/incidents/{id}/status': 'Change the status',
  'PUT /api/incidents/{id}/priority': 'Change the priority (Manager or Admin)',
  'GET /api/incidents/{id}/history': 'Incident history',
  'GET /api/incidents/{incidentId}/comments': 'List comments',
  'POST /api/incidents/{incidentId}/comments': 'Add a comment',
  'GET /api/categories': 'List categories',
  'POST /api/categories': 'Create a category (Admin)',
  'PUT /api/categories/{id}': 'Edit or hide a category (Admin)',
  'GET /api/users': 'List users (Manager or Admin)',
  'POST /api/users': 'Create a user and email them an invitation (Admin)',
  'GET /api/users/{id}': 'Get one user (Manager or Admin)',
  'PUT /api/users/{id}/role': 'Change a role (Admin)',
  'PUT /api/users/{id}/active': 'Deactivate or reactivate (Admin)',
  'POST /api/users/{id}/password-reset-email': 'Email a user a password reset link (Admin)',
  'GET /api/sla/policies': 'List SLA policies (Manager or Admin)',
  'PUT /api/sla/policies/{priority}': 'Edit an SLA policy (Admin)',
  'GET /api/dashboard/summary': 'Dashboard: right now',
  'GET /api/dashboard/incident-statistics': 'Dashboard: the last N days',
};

const OPEN = new Set([
  'POST /api/auth/register',
  'POST /api/auth/login',
  'POST /api/auth/refresh-token',
  'POST /api/auth/logout',
  'POST /api/auth/forgot-password',
  'POST /api/auth/reset-password',
]);

const FOLDER_ORDER = ['Auth', 'Incidents', 'Comments', 'Categories', 'Users', 'Sla', 'Dashboard'];
const FOLDER_TITLES = { Sla: 'SLA' };

// What to put in each field of an example request. Anything not listed gets a sensible default from its type.
const SAMPLES = {
  email: '{{email}}',
  password: '{{password}}',
  currentPassword: '{{password}}',
  newPassword: 'Str0ng-New-Pass!',
  token: 'paste the token from the email',
  refreshToken: '{{refreshToken}}',
  role: 'Employee',
  title: 'Printer on the 3rd floor is jammed',
  description: 'Paper keeps jamming in tray 2 since this morning.',
  categoryId: '{{categoryId}}',
  assigneeUserId: '{{userId}}',
  priority: 'High',
  status: 'InProgress',
  reason: 'Waiting for a replacement part.',
  text: 'I restarted it and the problem is still there.',
  isActive: true,
  name: 'Facilities',
  resolutionTargetHours: 8,
};

const PATH_VARIABLES = {
  id: (p) => (p.includes('/users/') ? '{{userId}}' : p.includes('/categories/') ? '{{categoryId}}' : '{{incidentId}}'),
  incidentId: () => '{{incidentId}}',
  priority: () => 'High',
};

// Query parameters that start switched on; every other filter is listed but off, so you can tick what you need.
const QUERY_ON = { Page: '1', PageSize: '20', Days: '30' };

// ---------------------------------------------------------------------------------------------------------------
// Turning a schema into an example body
// ---------------------------------------------------------------------------------------------------------------
function resolve(schema) {
  if (schema && schema.$ref) {
    return spec.components.schemas[schema.$ref.split('/').pop()];
  }
  return schema;
}

function typeOf(schema) {
  const t = schema.type;
  return Array.isArray(t) ? t.find((x) => x !== 'null') : t;
}

function sample(name, schemaIn) {
  if (name in SAMPLES) return SAMPLES[name];
  const schema = resolve(schemaIn) || {};
  if (schema.enum) return schema.enum[0];
  switch (typeOf(schema)) {
    case 'integer':
    case 'number':
      return schema.minimum ?? 1;
    case 'boolean':
      return true;
    case 'array':
      return [];
    case 'object':
      return exampleObject(schema);
    default:
      return schema.format === 'uuid' ? '00000000-0000-0000-0000-000000000000' : 'string';
  }
}

function exampleObject(schema) {
  const body = {};
  for (const [name, prop] of Object.entries(resolve(schema).properties || {})) {
    body[name] = sample(name, prop);
  }
  return body;
}

// ---------------------------------------------------------------------------------------------------------------
// Test scripts (run by Postman after each request)
// ---------------------------------------------------------------------------------------------------------------
const SAVE_TOKENS = [
  'const body = pm.response.json();',
  "pm.collectionVariables.set('accessToken', body.token);",
  "pm.collectionVariables.set('refreshToken', body.refreshToken);",
];

function testScript(key, method) {
  const lines = ['pm.test("Succeeds (status 2xx)", () => pm.expect(pm.response.code).to.be.within(200, 299));'];

  if (['POST /api/auth/login', 'POST /api/auth/register', 'POST /api/auth/refresh-token', 'POST /api/auth/change-password'].includes(key)) {
    lines.push('if (pm.response.code === 200) {', ...SAVE_TOKENS.map((l) => '  ' + l), '}');
  }
  if (key === 'POST /api/auth/logout') {
    lines.push("pm.collectionVariables.set('accessToken', '');", "pm.collectionVariables.set('refreshToken', '');");
  }
  if (key === 'POST /api/incidents') {
    lines.push("if (pm.response.code === 201) pm.collectionVariables.set('incidentId', pm.response.json().id);");
  }
  if (key === 'POST /api/categories') {
    lines.push("if (pm.response.code === 201) pm.collectionVariables.set('categoryId', pm.response.json().id);");
  }
  if (key === 'POST /api/users') {
    lines.push("if (pm.response.code === 201) pm.collectionVariables.set('userId', pm.response.json().id);");
  }
  if (key === 'GET /api/users') {
    lines.push("const users = pm.response.json();", "if (users.length) pm.collectionVariables.set('userId', users[0].id);");
  }
  if (key === 'GET /api/categories') {
    lines.push("const categories = pm.response.json();", "if (categories.length) pm.collectionVariables.set('categoryId', categories[0].id);");
  }
  if (key === 'GET /api/incidents') {
    lines.push("const page = pm.response.json();", "if (page.items && page.items.length) pm.collectionVariables.set('incidentId', page.items[0].id);");
  }
  return lines;
}

// ---------------------------------------------------------------------------------------------------------------
// Building the requests
// ---------------------------------------------------------------------------------------------------------------
function buildRequest(method, urlPath, op) {
  const key = `${method.toUpperCase()} ${urlPath}`;
  const name = NAMES[key] || key;

  const pathParts = urlPath.split('/').filter(Boolean).map((part) => {
    const match = /^\{(.+)\}$/.exec(part);
    return match ? PATH_VARIABLES[match[1]](urlPath) : part;
  });

  const query = (op.parameters || [])
    .filter((p) => p.in === 'query')
    .map((p) => ({ key: p.name, value: QUERY_ON[p.name] ?? '', disabled: !(p.name in QUERY_ON) }));

  const enabled = query.filter((q) => !q.disabled);
  const rawQuery = enabled.length ? '?' + enabled.map((q) => `${q.key}=${q.value}`).join('&') : '';

  const request = {
    method: method.toUpperCase(),
    header: [],
    url: {
      raw: `{{baseUrl}}/${pathParts.join('/')}${rawQuery}`,
      host: ['{{baseUrl}}'],
      path: pathParts,
      ...(query.length ? { query } : {}),
    },
  };

  const jsonBody = op.requestBody?.content?.['application/json']?.schema;
  if (jsonBody) {
    request.header.push({ key: 'Content-Type', value: 'application/json' });
    request.body = {
      mode: 'raw',
      raw: JSON.stringify(exampleObject(jsonBody), null, 2),
      options: { raw: { language: 'json' } },
    };
  }

  if (OPEN.has(key)) {
    request.auth = { type: 'noauth' };
  }

  return {
    key,
    item: {
      name,
      event: [{ listen: 'test', script: { type: 'text/javascript', exec: testScript(key, method) } }],
      request,
    },
  };
}

const byTag = {};
const byKey = {};
for (const [urlPath, operations] of Object.entries(spec.paths)) {
  for (const [method, op] of Object.entries(operations)) {
    const { key, item } = buildRequest(method, urlPath, op);
    (byTag[(op.tags || ['Other'])[0]] ||= []).push(item);
    byKey[key] = item;
  }
}

const folders = FOLDER_ORDER.filter((tag) => byTag[tag]).map((tag) => ({
  name: FOLDER_TITLES[tag] || tag,
  item: byTag[tag],
}));

// A guided tour: run this folder top to bottom with the Collection Runner for a quick end-to-end check.
const TOUR = [
  'POST /api/auth/login',
  'GET /api/dashboard/summary',
  'GET /api/categories',
  'POST /api/incidents',
  'GET /api/incidents/{id}',
  'POST /api/incidents/{incidentId}/comments',
  'GET /api/users',
  'PUT /api/incidents/{id}/assign',
  'PUT /api/incidents/{id}/status',
  'GET /api/incidents/{id}/history',
  'GET /api/incidents',
  'POST /api/auth/refresh-token',
  'POST /api/auth/logout',
];

const tour = {
  name: '00 Quick tour (run this folder in order)',
  description:
    'Fill in "email" and "password" in the environment with an Admin account (so every step is allowed), then run this folder ' +
    'with the Collection Runner. It signs in, creates an incident, reads it, comments, assigns it, starts work on it, reads the ' +
    'history, renews the login with the refresh token and signs out. Every step checks for a 2xx answer.',
  item: TOUR.map((key) => structuredClone(byKey[key])),
};

const collection = {
  info: {
    name: 'ResolveIQ API',
    description:
      'Every endpoint of the ResolveIQ API, generated from its OpenAPI description (tools/make-postman.js).\n\n' +
      '1. Import this collection and the "ResolveIQ local" environment, and choose that environment.\n' +
      '2. Put an account in the environment\'s "email" and "password" (they start empty on purpose).\n' +
      '3. Run "Log in" once. The tokens are saved for you, and every other request uses them.\n' +
      'The login token lasts 15 minutes; "Renew the login" swaps the refresh token for a new one.',
    schema: 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json',
  },
  auth: { type: 'bearer', bearer: [{ key: 'token', value: '{{accessToken}}', type: 'string' }] },
  variable: [
    { key: 'baseUrl', value: 'http://localhost:5293' },
    { key: 'accessToken', value: '' },
    { key: 'refreshToken', value: '' },
    { key: 'incidentId', value: '' },
    { key: 'userId', value: '' },
    { key: 'categoryId', value: '' },
  ],
  item: [tour, ...folders],
};

const environment = {
  name: 'ResolveIQ local',
  values: [
    { key: 'email', value: '', type: 'default', enabled: true },
    { key: 'password', value: '', type: 'secret', enabled: true },
  ],
  _postman_variable_scope: 'environment',
};

fs.writeFileSync(path.join(outDir, 'ResolveIQ.postman_collection.json'), JSON.stringify(collection, null, 2) + '\n');
fs.writeFileSync(path.join(outDir, 'ResolveIQ.local.postman_environment.json'), JSON.stringify(environment, null, 2) + '\n');

const count = Object.keys(byKey).length;
console.log(`Wrote ${count} requests in ${folders.length} folders, plus a ${tour.item.length}-step quick tour, to ${outDir}`);
