// Makes a fresh, empty, throwaway database for the browser tests. It is never the everyday ResolveIQDb:
// it has its own name, and it is dropped and rebuilt every time, so every run starts from the same place.
const { spawnSync } = require('node:child_process');
const path = require('node:path');

const connection =
  process.env.E2E_CONNECTION ||
  'Server=(localdb)\\MSSQLLocalDB;Database=ResolveIQ_E2E;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True';

if (!/Database=ResolveIQ_E2E/i.test(connection)) {
  console.error('Refusing to run: the e2e database must be called ResolveIQ_E2E, so the real data is never touched.');
  process.exit(1);
}

const backend = path.resolve(__dirname, '..', '..', 'backend');
const common = ['--project', path.join(backend, 'ResolveIQ.Infrastructure'), '--startup-project', path.join(backend, 'ResolveIQ.API')];

function ef(args) {
  const result = spawnSync('dotnet', ['ef', ...args, ...common], {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    // The API reads its database address from this setting, so the tools use the throwaway one too.
    env: { ...process.env, ConnectionStrings__DefaultConnection: connection },
  });
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

ef(['database', 'drop', '--force']);
ef(['database', 'update']);
