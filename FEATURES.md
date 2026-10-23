# Workit — Feature List

Workit is a workforce management system for service contractors. This repository contains the **Owner App** (web, for company managers) and the shared REST API. The iOS employee app is maintained separately; see the [deploy and release runbook](deploy/RUNBOOK.md).

---

## Owner App

### Dashboard
- Monthly overview of company activity
- KPI cards: total hours, overtime hours, active customers, running jobs, active employees
- Daily load bar chart — visualizes hours worked per calendar day across the month
- Filter the entire dashboard by customer, job, or employee (combinable filter chips)
- Summary panels: top customers, top jobs, top team members by hours (bar charts)
- Recent activity table with date, employee, job, customer, and hours
- Month navigation (previous/next + month picker)

### Employee Management
- View all employees in a data grid (name, trade, SSN, email, phone, emergency contact)
- Add new employees with full details and an initial password
- Edit employee details inline
- Reset any employee's password (auto-generates a secure random password)
- Trades: Electrician, Carpenter, Plumber, Painter, Other

### Customer Management
- View all customers (name, SSN, email, phone, contact person)
- Add and edit customers inline

### Job Management
- Create jobs linked to a customer with a billing type (Hourly or Fixed Price)
- Expandable job cards showing:
  - Total hours, overtime, worker count, entry count, material cost
  - Hours breakdown by employee (with entry count and last work date)
  - Materials consumed (quantity and cost ex./incl. VAT per material)
- Edit job name, code, customer, and billing type

### Time Entry Management
- Log time on behalf of any employee: employee, job, date, regular hours, overtime, notes
- View all company time entries filtered by date range, employee, or job

### Materials Management
- Full material catalog: name, product code, category, unit, unit price (ex. VAT), VAT rate, stock quantity
- Add, edit, and deactivate materials
- Stock is automatically reduced when usage is logged
- Filter by category

### Tool Management
- Add tools with name, serial number, and description
- Edit tool details
- Delete tools (only if not currently assigned)
- View full assignment history (who has what tool and for how long)

### Payday Integration
- First-time company setup: import company details from Payday and auto-generate the owner account
- Sales invoices are stored in Workit: every Payday invoice (lines and payments included) is kept in Workit's database, refreshed every 30 minutes, when the Invoices page opens on a copy older than 5 minutes, and on "Refresh from Payday"; invoices created, edited or opened from Workit are saved at once, and drafts deleted in Payday drop out
- Purchase expenses are stored in Workit the same way: every Payday expense and its lines, refreshed with the invoices and on the Expenses page; Payday's purchase details follow Payday, while job links, typed sale prices and billings stay Workit's; a purchase bills at its Ein.verð (vendor's unit price without VAT, before our discount, no markup), each expense read the way its lines add up to its total; an expense Payday deleted drops out, or stays as DELETED (not billable) if something was already billed from it
- Customers: Workit's customers are the stored copy of Payday's (kept in step both ways every 30 minutes); the invoice pickers and the Customers page read them from Workit, and opening a customer stores Payday's latest
- Payday employees, the company profile and Payday users are stored in Workit and refreshed every 30 minutes; Workit employees are linked to Payday's by SSN (linked only — their details and app accounts are not touched; "Sync Employees" on the Payday page still imports on purpose); the Payday page shows the stored company and users when Payday can't be reached
- Payday's lookup lists (expense accounts, payment types, sales ledger accounts, pension funds) are stored in Workit too, refreshed every 30 minutes and on every read, and answer from Workit when Payday can't be reached; only statements, PDFs and attachments are still fetched live
- Invoice dashboard: total revenue, collected, unpaid, overdue amounts
- VAT owed and outstanding expenses
- Income statement chart (revenue vs. collected) with configurable date range

### Company Administration
- View company information (name, SSN, owner, email, phone, address)

---

## API

- JWT authentication with role-based access (Admin, Owner, Employee)
- Employees can only read/write their own data; Owners see all company data
- Endpoints for: auth, company, customers, employees, jobs, time entries, materials, material usage, tools, tool assignments
- PostgreSQL backend via Entity Framework Core

---

## Roles Summary

Employee permissions remain available through the API for the separately maintained iOS app.

| Capability | Owner | Employee |
|---|---|---|
| Company dashboard | Full (all staff) | Personal only |
| Manage employees | Create, edit, reset password | — |
| Manage customers | Full CRUD | — |
| Manage jobs | Full CRUD | Read-only |
| Log time entries | For any employee | Self only |
| Manage materials | Full CRUD | Browse + log usage |
| Manage tools | Full CRUD | Take / return |
| Payday integration | Full | — |
