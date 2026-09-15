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
