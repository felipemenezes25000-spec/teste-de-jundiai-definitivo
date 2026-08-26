import { expect, test } from '@playwright/test';

async function authenticate(page) {
  await page.goto('/login.html', { waitUntil: 'domcontentloaded' });
  await page.locator('#login').click();
  await expect(page.locator('#mfa')).toBeVisible();
  await page.locator('#verify').click();
  await expect(page.locator('#continue')).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem('jundiai.session'))).toBeTruthy();
}

const criticalRoutes = [
  '/',
  '/poc.html',
  '/verification.html',
  '/evidence-pack.html',
  '/dossier.html',
  '/contingency.html',
  '/command-center.html',
  '/caretrace.html',
  '/governance.html',
  '/registration.html',
  '/workforce.html',
  '/referrals.html',
  '/clinical-ops.html',
  '/agenda.html',
  '/telemedicine.html',
  '/immunization-v2.html',
  '/pharmacy-care.html',
  '/diagnostics.html',
  '/dental-v2.html',
  '/billing-v2.html',
  '/operations.html',
  '/citizen.html',
  '/esus.html',
  '/acs.html'
];

function collectNetworkProblems(page) {
  const problems = [];
  page.on('pageerror', error => problems.push(`pageerror: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error') problems.push(`console: ${message.text()}`));
  });
  page.on('response', response => {
    const status = response.status();
    const type = response.request().resourceType();
    const interesting = ['document', 'script', 'stylesheet', 'xhr', 'fetch'].includes(type);
    if (!interesting) return;
    if (status === 401 || status === 403 || status >= 500) {
      problems.push(`${type} ${status}: ${response.url()}`);
    }
  });
  return problems;
}

test('24 superfícies autenticadas não produzem 401/403/5xx com sessão administrativa válida', async ({ page }) => {
  expect(criticalRoutes).toHaveLength(24);
  await authenticate(page);
  const problems = collectNetworkProblems(page);

  for (const route of criticalRoutes) {
    problems.length = 0;
    const response = await page.goto(route, { waitUntil: 'domcontentloaded' });
    expect(response, `sem resposta documental para ${route}`).not.toBeNull();
    expect(response.status(), `documento ${route}`).toBeLessThan(400);
    await expect(page.locator('body')).toBeVisible();
    await page.waitForTimeout(450);
    expect(problems, `falhas de auth/runtime em ${route}`).toEqual([]);
  }
});

test('auth guard remove header demonstrativo legado e injeta Bearer', async ({ page }) => {
  await authenticate(page);
  await page.goto('/poc.html', { waitUntil: 'domcontentloaded' });

  const observed = await page.evaluate(async () => {
    const response = await fetch('/api/access/context', {
      headers: {
        'X-Demo-Role': 'acs',
        'X-Demo-User': 'forged.frontend'
      }
    });
    return { status: response.status, body: await response.json() };
  });

  expect(observed.status).toBe(200);
  expect(observed.body.authenticated).toBe(true);
  expect(observed.body.role).toBe('poc_admin');
  expect(observed.body.demoRoleHeaderEnabled).toBe(false);
});

test('401 limpa sessão e preserva rota de retorno no login', async ({ page }) => {
  await authenticate(page);
  await page.goto('/caretrace.html', { waitUntil: 'domcontentloaded' });

  await page.evaluate(() => localStorage.setItem('jundiai.session', 'sessao-expirada-forjada'));
  await page.evaluate(() => fetch('/api/care-trace/00000000-0000-0000-0000-000000000000'));
  await page.waitForURL(/\/login\.html\?next=/);

  const next = new URL(page.url()).searchParams.get('next');
  expect(next).toBe('/caretrace.html');
  expect(await page.evaluate(() => localStorage.getItem('jundiai.session'))).toBeNull();
});
