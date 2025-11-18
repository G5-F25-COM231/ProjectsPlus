# Story: Assign Supervisor to Project (Admin)

> As an Administrator, I can assign faculty supervisors to projects.

Iteration: 1  
Owner: Dev1 – Vision  

---

## 1. UX Requirements: Assignment Flow on Project Detail Page

Admins assign supervisors from the **Project Detail Page**.

### 1.1 Location on Page

A new section appears in the project detail view:

**Section Title: “Supervisor Assignment”**

Subsections:
- Current supervisor (if exists)
- “Assign Supervisor” button
- Search input to find supervisor
- Search results listing
- Confirmation modal
- Rollback/undo feedback

---

## 2. Assignment UI Details

### 2.1 When No Supervisor is Assigned

Show:
Supervisor: None assigned
[ Assign Supervisor ]


Clicking the button opens the search panel.

---

### 2.2 Search Panel (Find Supervisor)

Admin sees:

**Search Bar**
- Placeholder: “Search faculty by name or email…”
- Debounced 300–400ms

**Filters**
- Department dropdown  
- Role = Faculty only  
- Status = Active only  

**Results List**
Each result displays:
- Faculty name  
- Email  
- Department  
- Last active  
- “Assign” button  

If no results:
No supervisors found.
Try adjusting your search.


---

### 2.3 Assignment Confirmation Modal

When admin selects a supervisor:

Assign Supervisor
Are you sure you want to assign <faculty_name> as supervisor for this project?
This action will be logged.


---

## 3. Unassign Flow

If a supervisor is already assigned:

Show:

Supervisor: Prof. John Smith
[ Unassign ]


Clicking **Unassign** triggers:

### 3.1 Unassign Confirmation Popup

Remove Supervisor
Are you sure you want to unassign this supervisor?

The project will no longer have faculty oversight.


Buttons:
- **Confirm**
- **Cancel**

On success:
Supervisor removed.


---

## 4. Rollback & Undo Behavior

### 4.1 After Assignment

A lightweight undo message appears:

Supervisor assigned.
Undo?



If clicked:
- Reassign previous supervisor
- Message:
Supervisor restored.


Undo is available for **~10 seconds**.

---

## 5. Edge Cases & Constraints

### 5.1 Duplicate Prevention

If a supervisor is already assigned to a project, the UI must NOT allow:
- assigning the same supervisor again  
- showing “Assign” button on that supervisor  

Instead show:
Already assigned


### 5.2 Supervisor Capacity (future constraint)

If faculty can supervise only X projects:
- When limit reached, show badge:

Capacity Reached


- Disable “Assign”

### 5.3 Disabled or inactive supervisors

Do not show them in search results.

---

## 6. Accessibility Requirements

### 6.1 Keyboard Navigation
- All buttons and search fields are tabbable
- “Assign” / “Unassign” dialogs must focus on the first actionable button

### 6.2 ARIA Labels
- Assign button:
`aria-label="Assign <faculty name> as supervisor"`
- Unassign button:
`aria-label="Unassign current supervisor"`

### 6.3 Screen Reader Messaging
Success/undo actions announced via:
aria-live="polite"


### 6.4 Contrast
Modal buttons and text meet WCAG AA contrast guidelines.

---

## 7. Traceability

- User story: Admin assigns supervisors to projects
- UX implemented in future:
  - `Pages/Projects/Details.cshtml`
- Backend endpoints: Dev2
- UI & interaction logic: Dev3

