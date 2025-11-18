# Story: Search Projects (Student)

> As a student, I can search for projects.

Iteration: 1  
Owner: Dev1 – Vision  

---

## 1. Overview

The search experience allows students to quickly find relevant projects by title, tags, keywords, or categories.  
The focus of this UX spec is:
- Smooth search interaction (debouncing)
- Rich filtering (facets)
- Clear “no results” states
- Accessible and predictable behavior

---

## 2. Search Interaction (Debouncing)

### 2.1 Input Debouncing (UX)

To avoid triggering a search on every keystroke, the UI should:

- Wait **300–400ms** after the user stops typing.
- If the user continues typing, restart the timer.
- Only after the pause should the search request be sent.

This improves:
- Performance  
- Bandwidth  
- User experience (no flashing or jumpy results)

### 2.2 Visual Behavior During Debounce

- Show a subtle loading indicator inside the search bar OR below it.
- Do NOT show “0 results” while typing.
- Only show results once debounce completes.

---

## 3. Faceted Search (Facets)

Facets allow filtering projects by attributes.

### 3.1 Supported Facets

1. **Status**
   - All  
   - New  
   - Ongoing  
   - Completed

2. **Tags**
   - Web  
   - AI  
   - Mobile  
   - Database  
   - Beginner  
   - Advanced  
   (Tags will eventually be dynamic from the database.)

3. **Role (optional future)**
   - Created by me  
   - Joined by me  

### 3.2 UX Behavior

- Facets appear as dropdowns or pills near the search bar.
- Changing a facet triggers a new search request (debounced).
- Multiple facets can be combined.  
  Example:  
  *Status = Ongoing + Tag = AI*

### 3.3 Active Filters Display

- Active facets should appear as small removable pills:
  - e.g., `Status: Ongoing ×`

---

## 4. No-Results States

The system must guide the user when no projects match the search.

### 4.1 No Results After Searching

No matching projects found.
Try adjusting your search terms or removing filters.

### 4.2 No Projects in Selected Category

If filters return nothing:
No projects available in this category.
Try choosing a different filter or clearing all filters.

### 4.3 Invalid Filter Combination
The selected filter combination returned no results.
Try removing one or more filters.


---

## 5. Search Result Display (Basic UX)

Although the UI build is Dev3’s responsibility, Dev1 defines the expected display:

Each result card should include:
- Title  
- Summary (1–2 lines)  
- Tags  
- Status (badge)  
- Action button:
  - “View details” or “Apply to join”

Cards must not shift layout during loading.

---

## 6. Loading States

Before search results appear, show:

Loading search results…


Or skeleton placeholders (gray boxes).

---

## 7. Accessibility Requirements

### 7.1 Search Input
- `aria-label="Search projects"`  
- Works with keyboard navigation.

### 7.2 Announcement of Result Count
Screen reader announces:
X projects found

when results update.

### 7.3 Facet Controls
- Each facet should have descriptive ARIA labels:
  - `aria-label="Filter by status"`
  - `aria-label="Filter by tags"`

### 7.4 Contrast
- Text and controls meet WCAG AA contrast ratio (4.5:1).

---

## 8. Traceability

- User story: Student can search for projects.
- UX implemented in:
  - `Pages/Projects/Index.cshtml` (search bar & filters)
- Backend search logic by Dev2
- Frontend search results and UI by Dev3

