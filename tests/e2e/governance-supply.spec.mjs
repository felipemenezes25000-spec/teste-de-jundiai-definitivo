import { expect, test } from '@playwright/test';

async function authenticate(page) {
  await page.goto('/login.html', { waitUntil: 'domcontentloaded' });
  await page.locator('#login').click();
  await expect(page.locator('#mfa')).toBeVisible();
  await page.locator('#verify').click();
  await expect(page.locator('#message')).toContainText('Autenticado como admin.jundiai');
}

test('Governança expõe inventário POC, provenance e limitação de SBOM sem mascarar gaps', async ({ page }) => {
  await authenticate(page);
  await page.goto('/governance.html', { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: 'Supply chain' }).click();

  await expect(page.locator('#supply')).toHaveClass(/active/);
  await expect(page.locator('#supply-kpis article')).toHaveCount(4);
  await expect(page.locator('#supply-list')).toContainText('Inventário POC de dependências');
  await expect(page.locator('#supply-list')).toContainText('NÃO É SBOM');
  await expect(page.locator('#supply-list')).toContainText('supply-chain.inventory.json');
  await expect(page.locator('#supply-list')).toContainText('lockfile npm absent');
  await expect(page.locator('#supply-list')).toContainText('Integridade runtime: OK');
  await expect(page.locator('#supply-list .mono').first()).toContainText(/inventory sha256 [a-f0-9]{64}/);
});

test('API protegida permanece fail-closed no browser sem sessão e header de papel não concede acesso', async ({ request }) => {
  const anonymous = await request.get('/api/sus/production');
  expect(anonymous.status()).toBe(401);
  const anonymousBody = await anonymous.json();
  expect(anonymousBody.role).toBe('anonymous');

  const forged = await request.get('/api/sus/production', { headers: { 'X-Demo-Role': 'poc_admin' } });
  expect(forged.status()).toBe(403);
  const forgedBody = await forged.json();
  expect(forgedBody.role).toBe('blocked_demo_header:poc_admin');
});
