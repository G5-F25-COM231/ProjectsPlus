using System;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    // Thrown when a DB uniqueness constraint is violated (e.g., duplicate email)
    public class DomainConflictException : Exception
    {
        public DomainConflictException(string message) : base(message) { }
    }

    // Thrown when optimistic concurrency version check fails
    public class DomainConcurrencyException : Exception
    {
        public DomainConcurrencyException(string message) : base(message) { }
    }

    // Generic not-found to simplify repo implementations/tests
    public class DomainNotFoundException : Exception
    {
        public DomainNotFoundException(string message) : base(message) { }
    }
}
