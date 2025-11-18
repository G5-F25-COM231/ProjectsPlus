# Story: View Projects (Student)

> As a student, I can view ongoing, completed, and new projects, as well as the projects I have joined.

Iteration: 1  
Owner: Dev1 – Vision  

---

## 1. UX Requirements

This page provides a student with an overview of all available projects, categorized into:
- **Ongoing**
- **Completed**
- **New**
- **My Projects (joined or created)**

The page also includes:
- Keyword search
- Filter dropdowns
- Project cards
- Empty/error states
- Loading state
- Accessibility requirements

---

## 2. List View UX Specification

### 2.1 Layout

- Page title: **"My Projects"**
- Subtitle describing purpose.
- Right side: **“+ Create New Project”** button.

### 2.2 Toolbar (Filters & Search)

Includes:
1. **Search bar**
   - Placeholder: “Search by title, tags, or keywords…”
2. **Status filter**
   - Dropdown: `All`, `New`, `Ongoing`, `Completed`
3. **Tag filter**
   - Dropdown: common tags (Web, AI, Mobile, Beginner, etc.)
4. **Quick tabs**
   - `All`
   - `Joined`
   - `Created by me`

### 2.3 Project Cards

Each card shows:
- Title
- Project summary (1–3 lines)
- Tags list
- Student's role (Creator / Contributor)
- Status badge:
  - Green = Ongoing  
  - Yellow = New  
  - Gray = Completed
- Actions:
  - View details
  - Apply to join
  - Open workspace (if joined)

---

## 3. Detail View UX (Basic)

A project detail page includes:
- Title
- Full description
- Tags
- Creator info
- Status
- GitHub URL (if provided)
- Actions:
  - Join project (if not joined)
  - Leave project (if joined)
  - Edit (creator only)

---

## 4. Empty States

### 4.1 When search has no results:

No projects match your search.  
Try removing filters or changing keywords.

### 4.2 When a category is empty:

You have no active projects yet.  
Click “Create New Project” to start one.

### 4.3 When the system has zero projects (rare case):

No projects have been created yet.

---

## 5. Error States

### 5.1 API failure or server error:

Unable to load projects at the moment.  
Please refresh or try again later.

### 5.2 Invalid filter use:

The selected filter is not available.

---

## 6. Loading State

Before projects appear, show skeleton loaders or:


The layout should not shift during loading.

---

## 7. Accessibility Checklist

### Keyboard Navigation
- All tabs, filters, and buttons support **TAB** navigation.
- Focus order matches the visible layout.
- “Create New Project” button is reachable by keyboard.

### ARIA Labels
- Search bar uses `aria-label="Search projects"`.
- Status indicators use `aria-label="Project status: ongoing/new/completed"`.

### Contrast Requirements
- Text contrast meets **WCAG AA (4.5:1)**.
- Status colors are supported by text labels (not color-only).

### Screen Reader Expectations
- Each project card is read as a **section** with:
  - Card title
  - Status
  - Actions (e.g., “View details for AI Study Planner”)

### Error Announcements
- Error messages appear in an `aria-live="polite"` region.

---

## 8. Traceability

- User story: Student can view new, ongoing, completed, and joined projects.
- UX implemented in:
  - `Pages/Projects/Index.cshtml`
  - `Pages/Projects/Details.cshtml` (later iteration)
- Backend/API: Dev2
- UI implementation: Dev3
