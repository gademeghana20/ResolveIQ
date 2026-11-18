// Used by "ng serve" (the development build) instead of public/config.js, so running the project on your own computer
// always talks to YOUR local API (http://localhost:5293) and never to the live demo.
// The production build (what Vercel deploys) keeps using public/config.js.
window.resolveiqConfig = {
  apiBaseUrl: '',
  demoPassword: '',
};
