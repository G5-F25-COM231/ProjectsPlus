# Story: Explore Public Projects (External Participant)

> As an External Participant, I want to explore publicly accessible projects.

Iteration: 1  
Owner: Dev1 – Vision  

---

## 1. Overview

External participants can:
- Browse a **public project directory**
- Open a **public project detail page**
- View redacted project information (with masked PII)
- See calls-to-action such as “Sign Up” or “Request to Mentor”
- Navigate a fully accessible, search-friendly interface

Public projects are strictly limited to **non-sensitive data**.

---

## 2. Public Directory UX Specification

### 2.1 Directory Layout

Page title: **“Explore Public Projects”**

Elements:
- Search bar (title/keywords)
- Filters:
  - Tags
  - Status (New / Ongoing / Completed)
  - Category (if used by team)
- Project grid view (cards)
- Empty/no-result states
- Pagination or “Load more”

### 2.2 Public Project Cards

Each card shows **only safe public data**:
- Project title  
- Short summary (non-sensitive)  
- Tags  
- Project status (ongoing/new/completed)  
- “View Project” CTA  

Information **NOT shown** to external users:
- Student names  
- Student emails  
- GitHub links  
- Internal IDs  
- Private project fields  

If creator name must be shown, use **redacted format**:
Created by: Student (name hidden)


---

## 3. Public Project Detail Page

### 3.1 Visible (Allowed) Data

External participants may see:
- Project title  
- High-level description (no sensitive details)  
- Tags  
- Status  
- Last updated date  
- Visible “Public” badge  
- Basic engagement CTAs:
  - “Sign Up to Join the Platform”
  - “Follow Updates”
  - “Request to Mentor” (future)

### 3.2 Hidden or Masked Data

The following must be hidden or redacted:

| Field | External View |
|-------|----------------|
| Creator Name | “Hidden for privacy” |
| Creator Email | Not shown |
| Team Members | Not shown |
| GitHub repo | Not shown unless explicitly configured public |
| Join requests | Not allowed |
| Internal IDs | Never shown |

If a field is unavailable, do NOT show a blank line — just omit it.

---

## 4. Redaction Rules (PII Masking)

### 4.1 Masked PII Examples

If a student’s email would appear, replace with:
student***@hidden


If a name would appear:
Name hidden for privacy


### 4.2 Sensitive Areas That Must Be Removed

- Student biography sections  
- Audit logs  
- Contribution history  
- Attached files  
- Links to internal dashboards  
- Mentor chat/comments  

Only **safe, public-facing** content is allowed.

---

## 5. Directory Search & Filters

### 5.1 Search Behavior

Search bar:
- Debounced 300–400ms  
- Searches title, tags, keywords  
- Does NOT search private fields  

### 5.2 Filters

Filters available to external visitors:

- Tags  
- Status  
- Category (if applicable)  

### 5.3 No Results State

If nothing matches:
No public projects found.
Try adjusting your search or filters.


---

## 6. Accessibility Checklist

### 6.1 Structure

- Page uses semantic elements (`<main>`, `<section>`, `<nav>`)  
- HEAD contains proper `title` & `description` for SEO  

### 6.2 ARIA Labels

Search input:
aria-label="Search public projects"


Project cards:
aria-label="Public project: <title>"


CTA actions:
aria-label="Sign up to join the platform"
aria-label="View project details"


### 6.3 Screen Reader Behavior

When results change:
aria-live="polite"

Announce:
X public projects found


### 6.4 Contrast

- All text must meet **WCAG AA (4.5:1)**  
- Status badges must use **color + text**  

### 6.5 Keyboard Navigation

- Cards fully tabbable  
- CTAs tabbable  
- Filters tabbable  
- Focus outline always visible  

---

## 7. CTA (Call-To-Action) Behavior

Because external participants are not authenticated:

### 7.1 Sign Up Flow
Button:
[ Sign Up ]

Redirects to registration page.

### 7.2 Follow/Watch Project (Future)
If implemented:
- Redirect unauthenticated users to sign-up

### 7.3 Request to Mentor (Future)
If implemented:
- Redirect to login/signup  
- Show non-logged-in message:
Please sign up to request mentorship.

---

## 8. Loading States

Before loading results, show:

Loading public projects…


Or skeleton loaders.

---

## 9. Traceability

- User story: External participant explores public projects
- UX implemented in future:
  - `Pages/Public/Directory.cshtml`
  - `Pages/Public/Project.cshtml`
- Public/visibility logic by Dev2
- UI implementation by Dev3

