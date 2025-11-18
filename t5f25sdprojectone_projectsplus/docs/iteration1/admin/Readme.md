# Story: Admin – Manage User Accounts and Permissions

> As an Administrator, I can manage all user accounts and permissions.

Iteration: 1  
Owner: Dev1 – Vision  

---

## 1. UX Requirements: Admin User List

The admin control panel allows the administrator to:
- View all users
- Filter by role or status
- Perform bulk actions (assign roles, disable accounts)
- Access detailed user information
- Trigger role updates and permission changes

### 1.1 Page Layout

**Header**
- Title: **"Admin: User Management"**
- Subtitle explaining purpose

**Toolbar**
- Search bar (search by name, email, or ID)
- Filter dropdown:
  - Role (Student, Admin, Faculty)
  - Status (Active, Disabled)
- Bulk actions menu:
  - Assign role
  - Disable selected accounts
  - Re-enable accounts
  - Remove users (future)

**User Table Columns**
- Checkbox (for bulk actions)
- Name
- Email
- Role(s)
- Status
- Last active date
- Actions:
  - View details
  - Change role
  - Disable / Enable user

### 1.2 User Detail Drawer/Page

Shown when admin selects **“View details”**.

Contains:
- Name
- Email
- Current roles
- Permissions overview
- Account status
- Assigned projects
- Action buttons:
  - “Add role”
  - “Remove role”
  - “Disable account”
  - “Enable account”

### 1.3 Empty States

If system has no users:
No user accounts found.


If search returns nothing:
No matching users found.
Try adjusting your search.


---

## 2. RBAC Specification (Role-Based Access Control)

This section defines the rules Dev2 and Dev3 follow.

### 2.1 Roles

| Role | Can View Other Profiles | Manage Roles | Manage Users | Join Projects | Create Projects |
|------|--------------------------|--------------|--------------|----------------|-----------------|
| Student | Limited | ✖ | ✖ | ✔ | ✔ |
| Faculty/Supervisor | Limited | ✖ | ✖ | ✔ | ✖ |
| Admin | ✔ | ✔ | ✔ | ✔ | ✔ |

### 2.2 Permissions

Admin can:
- Add/remove roles from any user
- Enable or disable user accounts
- View all profile information
- Access audit logs
- Perform bulk updates

Students CANNOT:
- Change roles
- Access admin dashboard
- Modify account statuses

### 2.3 RBAC Enforcement (UI-Level Expectations)

- Role-change buttons appear **only** for Admin role.
- Disabled users must show:
Status: Disabled

- Admin UI must not show unauthorized controls.

---

## 3. Bulk Action Behaviour

### 3.1 Selecting Multiple Users

- Each row has a checkbox.
- Admin can select:
- One
- Many
- “Select All”

### 3.2 Bulk Actions

**Assign Role (bulk)**  
Admin picks from a dropdown (Student, Admin, Faculty).

**Disable Accounts (bulk)**  
Prompt:
Are you sure you want to disable selected accounts?
These users will lose access immediately.


**Enable Accounts (bulk)**  
Use for previously disabled users.

**Remove Role (bulk)** (optional future)
Not required in Iteration 1.

---

## 4. Admin Runbook (Operations Guide)

Describes how an admin is expected to operate the system.

### 4.1 How to change a user’s role

1. Search or find the user in the table  
2. Open user actions → “Change Role”  
3. Select new role  
4. Confirm action  
5. UI shows:

Role updated successfully


### 4.2 How to disable a user account

1. Select user(s)  
2. Click “Disable Account”  
3. Confirm  
4. Disabled users appear greyed out

### 4.3 How to re-enable a user account

1. Filter: “Status: Disabled”  
2. Select account  
3. Click “Enable Account”  
4. Confirm  
5. User returns to Active state

### 4.4 How to view audit events (future)

Not required in iteration 1, but included for clarity.

---

## 5. Accessibility Requirements

- All table rows must be keyboard navigable  
- Checkbox selection uses:
- `aria-label="Select user <name>"`
- Bulk actions menu has:
- `aria-label="Bulk actions for selected users"`
- High contrast mode supported  
- Disabled users must provide both visual + text indicators (not color alone)

Screen reader example:
User John Doe, role Student, status Active, actions available.


---

## 6. Traceability

- User story: Admin manages users and permissions.
- UX implemented in future:
  - `Pages/Admin/Users.cshtml`
- RBAC logic enforced by Dev2  
- UI implementation by Dev3

