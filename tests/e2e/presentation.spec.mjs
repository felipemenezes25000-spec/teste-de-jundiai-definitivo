import { expect, test } from '@playwright/test';

async function authenticate(page) {
  await page.goto('/login.html', { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveTitle(/Jundiaí HealthOS/);
  await expect(page.locator('#username')).toHaveValue('admin.jundiai');
  await page.locator('#login').click();
  await expect(page.locator('#mfa')).toBeVisible();
  await page.locator('#verify').click();
  await expect(page.locator('#continue')).toBeVisible();
  await expect(page.locator('#message')).toContainText('Autenticado como admin.jundiai');
  const token = await page.evaluate(() => localStorage.getItem('jundiai.session'));
  expect(token).toBeTruthy();
}

function browserProblems(page) {
  const problems = [];
  page.on('pageerror', error => problems.push(`pageerror: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error') problems.push(`console: ${message.text()}`);
  });
  page.on('response', response => {
    const type = response.request().resourceType();
    if (response.status() >= 400 && ['document', 'script', 'stylesheet'].includes(type)) {
      problems.push(`http ${response.status()}: ${response.url()}`);
    }
  });
  return problems;
}

async function streamToBuffer(stream) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(Buffer.from(chunk));
  return Buffer.concat(chunks);
}

test('login + MFA + Preparar Banca deixam o cockpit READY', async ({ page }) => {
  await authenticate(page);
  await page.locator('#continue').click();
  await expect(page).toHaveURL(/\/poc\.html$/);
  await expect(page.getByRole('heading', { name: /Uma apresentação guiada pelos 14 blocos/i })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Evidence Pack', exact: true }).first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Dossiê da Banca', exact: true }).first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Kit de Contingência', exact: true }).first()).toBeVisible();

  await page.locator('#prepare-presentation').click();
  await expect(page.locator('#presentation-status')).toContainText('READY · banca preparada');
  await expect(page.locator('#presentation-status')).toContainText('8/8');
  await expect(page.locator('#presentation-status')).toContainText('Runner 14/14');
  await expect(page.locator('#presentation-status')).toContainText('Evidence Pack SHA-256');
});

test('Evidence Pack é gerado, verificado e exportado pelo navegador', async ({ page }) => {
  await authenticate(page);
  await page.goto('/evidence-pack.html', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: /14 blocos, evidências, dependências e hash/i })).toBeVisible();

  await page.locator('#generate').click();
  await expect(page.locator('#summary')).toContainText('14/14');
  await expect(page.locator('#blocks .pack-block')).toHaveCount(14);
  await expect(page.locator('#pack-meta')).toContainText('SHA-256');
  await expect(page.locator('#blockers')).toContainText('HAB-AT-29');

  await page.locator('#verify').click();
  await expect(page.locator('#pack-meta')).toContainText('INTEGRIDADE APROVADA');
  await expect(page.locator('#pack-meta')).toContainText('package hash OK');
  await expect(page.locator('#pack-meta')).toContainText('Evidence Ledger OK');

  const downloadPromise = page.waitForEvent('download');
  await page.locator('#export').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^jundiai-rce-008-2026-evidence-pack-.*\.json$/);
  const stream = await download.createReadStream();
  const exported = JSON.parse((await streamToBuffer(stream)).toString('utf8'));
  expect(exported.packageSha256).toMatch(/^[a-f0-9]{64}$/);
  expect(exported.payload.verification.passedBlocks).toBe(14);
  expect(exported.payload.verification.totalBlocks).toBe(14);
  expect(exported.payload.blocks).toHaveLength(14);
  expect(exported.payload.nonCodeBlockers.some(item => item.id === 'HAB-AT-29')).toBe(true);
});

test('Dossiê da Banca congela READY, build, runtime e Evidence Pack em artefato verificável', async ({ page }) => {
  await authenticate(page);
  await page.goto('/dossier.html', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: /Dossiê verificável do estado demonstrado/i })).toBeVisible();

  await page.locator('#generate').click();
  await expect(page.locator('#dossier-status')).toContainText('READY');
  await expect(page.locator('#dossier-status')).toContainText('14/14');
  await expect(page.locator('#dossier-status')).toContainText('8/8');
  await expect(page.locator('#verification-code')).toHaveText(/^JUN-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}$/);
  await expect(page.locator('#blocks .block-line')).toHaveCount(14);
  await expect(page.locator('#checks .module')).toHaveCount(8);
  await expect(page.locator('#blockers')).toContainText('HAB-AT-29');
  await expect(page.locator('#hash-proof')).toContainText('Evidence Pack');
  await expect(page.locator('#hash-proof')).toContainText('Manifesto runtime');
  await expect(page.locator('#verification-result')).toContainText('INTEGRIDADE APROVADA');
  await expect(page.locator('#verification-result')).toContainText('bytes runtime');
  await expect(page.locator('#verification-result')).toContainText('OK');

  const code = await page.locator('#verification-code').textContent();
  const downloadPromise = page.waitForEvent('download');
  await page.locator('#export').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^jundiai-rce-008-2026-dossie-JUN[A-F0-9]{12}\.json$/);
  const stream = await download.createReadStream();
  const exported = JSON.parse((await streamToBuffer(stream)).toString('utf8'));
  expect(exported.verificationCode).toBe(code);
  expect(exported.dossierSha256).toMatch(/^[a-f0-9]{64}$/);
  expect(exported.payload.preflight.ready).toBe(true);
  expect(exported.payload.preflight.passedBlocks).toBe(14);
  expect(exported.payload.preflight.totalBlocks).toBe(14);
  expect(exported.payload.evidencePack.payload.blocks).toHaveLength(14);
  expect(exported.payload.build.service).toBe('Jundiai HealthOS');
  expect(exported.payload.build.contract).toBe('RCE 008/2026');
  expect(exported.payload.release.manifestSha256).toMatch(/^[a-f0-9]{64}$/);
  expect(exported.payload.release.payload.runtimeArtifactsComplete).toBe(true);
  expect(exported.payload.release.payload.files).toHaveLength(3);
  expect(exported.payload.release.payload.files.every(item => item.exists && /^[a-f0-9]{64}$/.test(item.sha256))).toBe(true);
  expect(exported.payload.preflight.nonCodeBlockers.some(item => item.id === 'HAB-AT-29')).toBe(true);
  if (process.env.GITHUB_SHA) expect(exported.payload.build.sourceRevision).toBe(process.env.GITHUB_SHA);
});

test('Kit de Contingência é gerado, verificado e baixado como ZIP pelo navegador', async ({ page }) => {
  await authenticate(page);
  await page.goto('/contingency.html', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: /ZIP autocontido e verificável para sobreviver sem rede/i })).toBeVisible();

  await page.locator('#generate').click();
  await expect(page.locator('#verification-code')).toHaveText(/^KIT-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}$/);
  await expect(page.locator('#summary')).toContainText('6');
  await expect(page.locator('#manifest-hash')).toHaveText(/^[a-f0-9]{64}$/);
  await expect(page.locator('#zip-hash')).toHaveText(/^[a-f0-9]{64}$/);
  await expect(page.locator('#checks')).toContainText('KIT APROVADO');
  await expect(page.locator('#checks')).toContainText('Dossiê íntegro');

  const downloadPromise = page.waitForEvent('download');
  await page.locator('#download').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^jundiai-rce-008-2026-contingencia-KIT[A-F0-9]{12}\.zip$/);
  const stream = await download.createReadStream();
  const bytes = await streamToBuffer(stream);
  expect(bytes.length).toBeGreaterThan(1000);
  expect(bytes.subarray(0, 4).toString('hex')).toBe('504b0304');
});

test('superfícies críticas da apresentação renderizam sem erro fatal de browser', async ({ page }) => {
  await authenticate(page);
  const problems = browserProblems(page);
  const routes = [
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

  for (const route of routes) {
    problems.length = 0;
    const response = await page.goto(route, { waitUntil: 'domcontentloaded' });
    expect(response, `sem resposta para ${route}`).not.toBeNull();
    expect(response.status(), `status da página ${route}`).toBeLessThan(400);
    await expect(page.locator('body')).toBeVisible();
    await page.waitForTimeout(300);
    expect(problems, `problemas em ${route}`).toEqual([]);
  }
});
