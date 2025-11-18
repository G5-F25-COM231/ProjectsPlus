# Story: Create Project (Student)

> As a student, I can create a new project for other students to join.

Iteration: 1  
Owner: Dev1 – Vision  

---

## 1. Acceptance Criteria

A student can create a new project with required fields, validation, and a clear submit flow.

### Functional Criteria
- Student can open a **Create Project** page.
- Form must include:
  - Title (required)
  - Short description (required)
  - Detailed description (optional)
  - Tags (multi-select)
  - Visibility: Public or Internal
- Form has required-field validation.
- User receives success confirmation after creating a project.
- If errors occur, show clear error messages.
- Redirect student to:
  - **Project Detail Page** after successful creation  
  OR  
  - Stay on the same page if validation fails.

### Non-Functional Criteria
- Form loads within 1 second.
- Accessible to keyboard-only users.
- Conforms to WCAG AA contrast.
- Error messages use `aria-live="polite"`.

---

## 2. UX Wireframe Description

### 2.1 Layout

**Page Title:**  
“Create New Project”

**Form Sections:**
1. **Project Title**  
   - Input box  
   - Required  
   - Inline validation: “Title is required”

2. **Short Description**  
   - Textarea (2–3 lines)  
   - Required, inline validation  

3. **Full Description**  
   - Rich-text or plain textarea  
   - Optional  

4. **Tags**  
   - Multi-select dropdown  
   - Suggested tags: AI, Web, Mobile, Java, Database  

5. **Visibility**  
   - Radio:
     - Public (anyone can view)
     - Internal (Centennial users only)

6. **Submit / Cancel Buttons**

**Success Message:**  
Project created successfully.


**Error State:**  
Unable to create project. Please try again.


---

## 3. Submit Flow

### 3.1 Valid Submission
- User clicks **Create Project**
- Form validates required fields
- API call succeeds → project saved
- Show:
Project created successfully.

- Redirect to Project Detail page

### 3.2 Client Validation Failure
Highlight invalid fields:
Please fill all required fields.


### 3.3 Server/API Failure
Show error message:
Something went wrong. Please try again later.


---

## 4. Error & Empty States

### 4.1 Required Field Errors
- Title or short description empty → show inline error under input
- Submit disabled until required fields are completed

### 4.2 Network/API Failure
Cannot create project right now.
Check your connection or try again.


---

## 5. Accessibility Checklist

### Keyboard Navigation
- All form fields tabbable in order
- Buttons accessible with keyboard
- Focus state clearly visible

### ARIA Labels
- Input fields include descriptive labels
- Submit button:
aria-label="Create project"


### Announcements (screen readers)
Error messages inside:
aria-live="polite"
### Contrast
- All labels and text respect WCAG AA (4.5:1)
- Buttons use color + text (never color-only)

---

## 6. API Contract (Final Draft)

**Endpoint:**  
`POST /api/projects`

**Request Body (JSON):**
```json
{
  "title": "AI Study Planner",
  "shortDescription": "A tool to help students plan study schedules",
  "fullDescription": "Optional extended description",
  "tags": ["AI", "Productivity"],
  "visibility": "Public"
}
Response:
{
  "projectId": "uuid",
  "status": "created"
}

Validation Errors:
{
  "error": "Title is required"
}

7. Traceability

User story: student creates projects

UX implemented in:

Pages/Projects/Create.cshtml

Backend implemented by Dev2

UI validation implemented by Dev3


---




