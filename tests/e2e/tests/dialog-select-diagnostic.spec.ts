import { test, expect } from '@playwright/test';

test('account dialog: selects open AND actions stay below content', async ({ page }) => {
  test.setTimeout(60_000);

  await page.goto('/accounts');
  await page.waitForLoadState('networkidle');

  // Pick a family that has owners so the selects have options.
  await page.locator('mat-toolbar mat-select').click();
  await page.getByRole('option', { name: 'Effler-Tate', exact: true }).click();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(300);

  const editFirst = page.locator('mat-row [aria-label^="Edit"]').first();
  if (await editFirst.count() === 0) {
    await page.getByRole('button', { name: 'New' }).click();
  } else {
    await editFirst.click();
  }
  await expect(page.locator('mat-dialog-container')).toBeVisible();
  await page.waitForTimeout(500);

  // 1. Check that all three selects open with a correctly positioned panel.
  const selects = await page.locator('mat-dialog-container mat-select').all();
  for (let i = 0; i < selects.length; i++) {
    const label = await selects[i].evaluate((el) =>
      el.closest('mat-form-field')?.querySelector('mat-label')?.textContent ?? '?'
    );
    const triggerBox = await selects[i].locator('.mat-mdc-select-trigger').boundingBox();
    await selects[i].locator('.mat-mdc-select-trigger').click();
    await page.waitForTimeout(250);
    const expanded = await selects[i].getAttribute('aria-expanded');

    // Find the select panel (any cdk-overlay-pane that isn't the dialog).
    let paneRect: { top: number; left: number; w: number; h: number } | null = null;
    const paneCount = await page.locator('.cdk-overlay-pane').count();
    for (let p = 0; p < paneCount; p++) {
      const pane = page.locator('.cdk-overlay-pane').nth(p);
      if (await pane.evaluate((el) => el.classList.contains('mat-mdc-dialog-panel'))) continue;
      paneRect = await pane.evaluate((el) => {
        const r = el.getBoundingClientRect();
        return { top: r.top, left: r.left, w: r.width, h: r.height };
      });
      break;
    }
    console.log(`"${label}": expanded=${expanded}, trigger=${JSON.stringify(triggerBox)}, panel=${JSON.stringify(paneRect)}`);

    expect(expanded).toBe('true');
    expect(paneRect).not.toBeNull();
    expect(paneRect!.top).toBeGreaterThan(50);   // not at (0,0)
    expect(paneRect!.left).toBeGreaterThan(50);

    await page.keyboard.press('Escape');
    await page.waitForTimeout(200);
  }

  // 2. Check that the actions row does NOT overlap the form content.
  const contentBox = await page.locator('mat-dialog-container mat-dialog-content').boundingBox();
  const actionsBox = await page.locator('mat-dialog-container mat-dialog-actions').boundingBox();
  const lastFieldBox = await page.locator('mat-dialog-container mat-form-field').last().boundingBox();

  console.log(`content: ${JSON.stringify(contentBox)}`);
  console.log(`actions: ${JSON.stringify(actionsBox)}`);
  console.log(`last field: ${JSON.stringify(lastFieldBox)}`);

  // Actions row should start at or below the content area's bottom (no overlap).
  expect(actionsBox!.y).toBeGreaterThanOrEqual(contentBox!.y + contentBox!.height - 1);
});
