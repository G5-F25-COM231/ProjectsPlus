# Story: View Profile (Student)

> As a student, I can view my profile including personal information and my projects that I have joined.

Iteration: 1  
Owner: Dev1 – Vision  

---

## 1. UX Requirements: Profile Layout

The student profile page includes a clear, accessible layout showing personal information and all projects associated with the student.

### 1.1 Page Structure

**Header Section**
- Student name (full name)
- Program / role (e.g., Student – Software Engineering Technology)
- Optional avatar placeholder
- “Edit Profile” button (future iteration)

**Profile Information Section**
- Email (masked depending on privacy settings)
- Program / Major
- Year of study (optional future)
- GitHub handle / portfolio links (if provided)
- Biography (optional)

**Projects Section**
- **Active Projects** (joined or created)
- **Completed Projects**
- Each project displayed as a card:
  - Title
  - Short description
  - Status badge (Ongoing, Completed)
  - Role: Creator / Contributor
  - Link to “View details”

Empty state examples:
You have no active projects yet.
Join or create a project to get started.


---

## 2. Privacy Exposure Matrix

This matrix defines what profile fields are visible to different user roles.

| Field | Student (self) | Other Students | Faculty/Supervisors | Admin | External Participants |
|-------|----------------|----------------|----------------------|--------|-------------------------|
| Full Name | ✔ | ✔ | ✔ | ✔ | ✔ (public projects only) |
| Email | ✔ | ✖ (masked) | ✔ | ✔ | ✖ |
| Program/Major | ✔ | ✔ | ✔ | ✔ | ✔ |
| Biography | ✔ | ✔ | ✔ | ✔ | ✖ |
| Joined Projects | ✔ | ✔ | ✔ | ✔ | Public projects only |
| Created Projects | ✔ | ✔ | ✔ | ✔ | Public projects only |
| GitHub URL | ✔ | ✔ | ✔ | ✔ | ✖ (unless public project) |
| Internal ID (UserId) | ✔ | ✖ | ✖ | ✔ | ✖ |

**Masking Example:**
- Other students see email as:  
  `v*****@my.centennialcollege.ca`

**External participants** see **only**:
- Publicly accessible projects
- Public project owner name
- Public project description  
They do **not** see:
- Student email  
- GitHub URL  
- Private projects  
- Internal system identifiers  

---

## 3. Privacy Behavior (UX)

- If a field is hidden (due to exposure rules), the UI should **not show it at all**.
- No “broken” empty labels should appear.
- Sensitive fields (email, GitHub, internal IDs) only appear to roles that are allowed to view them.

**Example:**  
If an external user opens a public project:
- Show the creator’s name and project description.
- Do NOT show their email or GitHub.

---

## 4. Accessibility Requirements

### 4.1 Structure & Navigation
- Profile page supports **full keyboard navigation**.
- Main sections have ARIA roles:
  - `role="region" aria-label="Profile Information"`
  - `role="region" aria-label="Projects"`

### 4.2 Screen Reader Labels
- Name heading uses `<h1>` for proper reading order.
- Each project card:
  - Must be announced as:
    ```
    Project: <project title>, status ongoing, role contributor
    ```
  - “View details” button should have:
    `aria-label="View details for <project title>"`

### 4.3 Contrast Standards
- All text meets **WCAG AA (4.5:1)**.
- Status badges use:
  - Color + text  
  (Never color-only — accessible for color-blind users.)

### 4.4 Focus Indicators
- Editable and interactive parts (tabs, buttons, links) must have visible focus outlines.

### 4.5 Error Messages
If profile data fails to load:
Unable to load profile information.
Please try again later.

This must be placed inside an `aria-live="polite"` region so screen readers announce it.

---

## 5. Loading States (Optional UX Specification)

While profile information loads:
- Show skeleton placeholders for:
  - Name line
  - Info lines
  - Project cards
- Do not shift layout when content appears.

Sample text:
Loading profile…


---

## 6. Traceability

- User story: Student views their profile and project history.
- UX implemented in:
  - `Pages/Profile/Index.cshtml` (or equivalent)
- Backend privacy logic delivered by Dev2
- UI and rendering of project cards by Dev3

