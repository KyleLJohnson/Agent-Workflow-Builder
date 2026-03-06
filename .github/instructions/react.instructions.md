---
applyTo: "client/**"
---

# React Development Instructions

## Component Architecture

- Use functional components as the standard for simplicity and composability.
- Extract reusable logic into custom hooks to promote code reuse and separation of concerns.
- Maintain a structured folder hierarchy to organize components, hooks, and utilities effectively.

## Type Safety

- Use TypeScript for type safety, better IDE support, and self-documenting code.

## State Management

- Implement state management using Context API or libraries like Redux for complex applications.
- Separate logic and design by organizing components and styles into distinct directories.

## Performance

- Optimize performance with lazy loading and code splitting to reduce bundle sizes.
- Use `React.memo` and `useCallback` to prevent unnecessary re-renders.
- Ensure unique keys for list items to avoid rendering issues.

## Error Handling

- Handle errors effectively using Error Boundaries and logging services like Sentry.

## Code Organization

- Use absolute imports for better readability and easier navigation.

## Testing

- Test your code with tools like Jest or React Testing Library to ensure reliability.

## Styling

- Integrate CSS-in-JS libraries or utility-first frameworks like Tailwind CSS for styling.
