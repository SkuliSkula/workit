# Workit — User Guide

---

## Owner App

The Owner App runs in a standard web browser. Log in at your company's owner URL (e.g. `admin.workit.is`).

### Logging in

1. Open the Owner App URL.
2. Enter your email and password.
3. Click **Log in**.

To log out, click the **Log out** button in the top navigation.

---

### Dashboard

The dashboard is the first screen after login. It shows a month-wide summary of your company's work.

**Changing the month**
Use the **‹** and **›** arrows to step through months, or click the month label to pick one from a calendar.

**Filtering**
Click any customer, job, or employee chip in the summary panels to filter the entire dashboard to that item. Multiple chips can be active at once. Click a chip again to remove the filter.

**Reading the panels**
- **KPI cards** (top row): quick totals for the selected month.
- **Daily load chart**: each column is one calendar day; height represents total hours worked that day.
- **Customer / Job / Team load**: horizontal bar charts showing relative workload. The top 6–8 items are shown.
- **Recent activity**: the latest time entries, regardless of filters.

---

### Employees

Navigate to **Employees** in the sidebar.

**Adding an employee**
1. Click **New employee**.
2. Fill in the employee's name, trade, SSN, email, phone, and emergency contact.
3. Enter an initial password (minimum 8 characters).
4. Click **Save**.
5. Share the email and password with the employee so they can log in to the separately maintained iOS employee app.

**Editing an employee**
1. Click the employee's row in the grid.
2. Update any fields in the side panel.
3. Click **Save**.

**Resetting a password**
1. Click the employee's row.
2. Click **Reset password**.
3. A new random password is generated and displayed once — copy it and share it with the employee.

---

### Customers

Navigate to **Customers** in the sidebar.

**Adding a customer**
1. Click **New customer**.
2. Enter name, SSN, email, phone, and contact person.
3. Click **Save**.

**Editing a customer**
Click the customer's row to open the edit panel, make changes, and click **Save**.

---

### Jobs

Navigate to **Jobs** in the sidebar.

**Creating a job**
1. Click **New job**.
2. Select a customer, enter a name, and choose a category and a billing type (Hourly or Fixed Price). The job code — the category's short code plus a running number, e.g. `REP007` — is assigned automatically and cannot be edited.
3. Click **Save**.

**Viewing job details**
Click the expand arrow on any job card to see:
- Summary KPIs (total hours, overtime, workers, material cost).
- **Workforce** — hours logged per employee.
- **Materials** — quantities and cost of materials used on the job.

**Editing a job**
Click the pencil icon on the job card, update the fields, and click **Save**.

---

### Time Entries

Navigate to **Time Entries** in the sidebar.

**Logging hours**
1. Click **New entry**.
2. Select the employee, the job, and the work date.
3. Enter regular hours and, if applicable, overtime hours.
4. Add optional notes.
5. Click **Save**.

**Filtering entries**
Use the **From / To** date pickers and the Employee / Job dropdowns to narrow the list.

---

### Materials

Navigate to **Materials** in the sidebar.

**Adding a material**
1. Click **New material**.
2. Fill in name, product code, category, unit, unit price (ex. VAT), VAT rate, and current stock quantity.
3. Click **Save**.

**Editing a material**
Click any material row to open the edit panel, adjust the fields, and click **Save**. Toggle the **Active** switch to deactivate a material without deleting it.

**Stock levels**
Stock is reduced automatically each time an employee logs material usage. Update the stock quantity manually here after a resupply.

---

### Tools

Navigate to **Tools** in the sidebar.

**Adding a tool**
1. Click **New tool**.
2. Enter a name, serial number, and description.
3. Click **Save**.

**Editing a tool**
Click the tool row, update the fields, and click **Save**.

**Deleting a tool**
A tool can only be deleted if it is not currently assigned to an employee. Return the tool first if needed.

**Viewing assignments**
The tool list shows which employee currently holds each tool and when they took it.

---

### Payday Integration

Navigate to **Payday** in the sidebar.

**First-time setup**
If the company has not been set up yet, Workit will offer to import your company details from Payday. Click **Set up from Payday**, review the imported data, and confirm. Your owner account credentials will be displayed once — save them.

**Invoice dashboard**
Select a **From** and **To** date range and click **Update** to refresh the numbers. The dashboard shows:
- Total invoiced, collected, unpaid, and overdue amounts.
- VAT owed and outstanding expenses.
- An income statement chart comparing revenue against collected amounts.

---

## iOS Employee App

The iOS employee app is maintained in a separate repository. See the [deploy and release runbook](deploy/RUNBOOK.md#ios-app) for details.
