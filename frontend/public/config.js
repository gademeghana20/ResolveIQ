// Settings for where this copy of the website runs. This file is public (anyone can open it), so never put a secret here.
//
//  apiBaseUrl    The web address of the ResolveIQ API, with no /api at the end.
//                Leave it empty when running on your own computer (the API is then http://localhost:5293).
//  demoPassword  Only for a public demo site: the shared password of the demo accounts. When it is filled in, the
//                sign-in page shows "Employee / Manager / Admin" demo buttons. Leave it empty everywhere else.
window.resolveiqConfig = {
  apiBaseUrl: 'https://resolveiq-api-demo.runasp.net',
  demoPassword: 'Password@123',
};
