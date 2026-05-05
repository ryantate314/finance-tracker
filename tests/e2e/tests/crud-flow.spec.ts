import { test, expect, Page, Locator } from '@playwright/test';

const FAMILY_NAME = `E2E Family ${Date.now()}`;
const OWNER_NAME = 'E2E Owner';
const ACCOUNT_NAME = 'E2E Checking';
const CATEGORY_NAME = 'E2E Food';
const SUB_CATEGORY_NAME = 'E2E Groceries';

// ── Toolbar ────────────────────────────────────────────────────────────────────

async function switchToFamily(page: Page, name: string) {
  await page.locator('mat-toolbar mat-select').click();
  await page.locator(`mat-option:has-text("${name}")`).click();
  await expect(page.locator('mat-toolbar mat-select')).toContainText(name);
}

// ── Dialog-based pages (Families, Owners, Accounts) ───────────────────────────

async function createViaDialog(page: Page, name: string) {
  await page.getByRole('button', { name: 'New' }).click();
  await page.locator('mat-dialog-container mat-form-field').filter({ hasText: 'Name' }).locator('input').fill(name);
  await page.locator('mat-dialog-container').getByRole('button', { name: 'Save' }).click();
  await expect(page.locator('mat-dialog-container')).not.toBeVisible();
}

async function deleteRow(page: Page, rowText: string) {
  const row = page.locator('mat-row').filter({ hasText: rowText });
  await row.locator('[aria-label^="Delete"]').click();
  await expect(row).not.toBeVisible();
}

// ── Categories page (accordion, inline editing) ────────────────────────────────

// Fills the visible inline input and confirms with the Save icon button.
async function fillInlineAndSave(container: Locator, value: string) {
  await container.locator('input').fill(value);
  await container.locator('[aria-label="Save"]').click();
}

async function createCategory(page: Page, name: string) {
  await page.getByRole('button', { name: /New Category/i }).click();
  const addRow = page.locator('.new-category-row');
  await fillInlineAndSave(addRow, name);
  await expect(page.locator('mat-expansion-panel').filter({ hasText: name })).toBeVisible();
}

async function createSubCategory(page: Page, categoryName: string, subName: string) {
  const panel = page.locator('mat-expansion-panel').filter({ hasText: categoryName });
  await panel.locator('mat-expansion-panel-header').click();
  const addBtn = panel.getByRole('button', { name: /Add sub-category/i });
  await expect(addBtn).toBeVisible();
  await addBtn.click();
  await fillInlineAndSave(panel.locator('.add-row'), subName);
  await expect(panel.locator('.sub-name', { hasText: subName })).toBeVisible();
}

async function deleteSubCategory(page: Page, categoryName: string, subName: string) {
  const panel = page.locator('mat-expansion-panel').filter({ hasText: categoryName });
  const subRow = panel.locator('.sub-row').filter({ hasText: subName });
  await subRow.locator('[aria-label="Delete sub-category"]').click();
  await expect(panel.locator('.sub-name', { hasText: subName })).not.toBeVisible();
}

async function deleteCategory(page: Page, name: string) {
  const panel = page.locator('mat-expansion-panel').filter({ hasText: name });
  await panel.locator('mat-expansion-panel-header [aria-label="Delete category"]').click();
  await expect(panel).not.toBeVisible();
}

// ── Tests ──────────────────────────────────────────────────────────────────────

test.describe('Basic CRUD flow', () => {
  test('create family, owner, account, and categories, then clean up', async ({ page }) => {
    // ── 1. Create family ──────────────────────────────────────────────────────
    await page.goto('/families');
    await createViaDialog(page, FAMILY_NAME);
    await expect(page.locator('mat-row').filter({ hasText: FAMILY_NAME })).toBeVisible();

    // ── 2. Switch to the new family ───────────────────────────────────────────
    await switchToFamily(page, FAMILY_NAME);

    // ── 3. Create owner ───────────────────────────────────────────────────────
    await page.goto('/owners');
    await createViaDialog(page, OWNER_NAME);
    await expect(page.locator('mat-row').filter({ hasText: OWNER_NAME })).toBeVisible();

    // ── 4. Create account ─────────────────────────────────────────────────────
    await page.goto('/accounts');
    await page.getByRole('button', { name: 'New' }).click();

    await page.locator('mat-dialog-container mat-form-field').filter({ hasText: 'Owner' }).locator('mat-select').click();
    await page.locator(`mat-option:has-text("${OWNER_NAME}")`).click();

    await page.locator('mat-dialog-container mat-form-field').filter({ hasText: 'Name' }).locator('input').fill(ACCOUNT_NAME);

    await page.locator('mat-dialog-container mat-form-field').filter({ hasText: 'Account Type' }).locator('mat-select').click();
    await page.locator('mat-option:has-text("Checking")').click();

    await page.locator('mat-dialog-container').getByRole('button', { name: 'Save' }).click();
    await expect(page.locator('mat-dialog-container')).not.toBeVisible();
    await expect(page.locator('mat-row').filter({ hasText: ACCOUNT_NAME })).toBeVisible();

    // ── 5. Create category and sub-category ───────────────────────────────────
    await page.goto('/categories');
    await createCategory(page, CATEGORY_NAME);
    await createSubCategory(page, CATEGORY_NAME, SUB_CATEGORY_NAME);

    // ── 6. Clean up ───────────────────────────────────────────────────────────
    await deleteSubCategory(page, CATEGORY_NAME, SUB_CATEGORY_NAME);
    await deleteCategory(page, CATEGORY_NAME);

    await page.goto('/accounts');
    await deleteRow(page, ACCOUNT_NAME);

    await page.goto('/owners');
    await deleteRow(page, OWNER_NAME);

    await page.goto('/families');
    await deleteRow(page, FAMILY_NAME);
  });
});
