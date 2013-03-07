
#if HAVE_HEAPTRACKER
#define HEAPTRACKED(stmt) do {          \
    try {                               \
        this.heapTracker.Enable ();     \
        stmt;                           \
    } finally {                         \
        HeapTracker.Disable ();         \
    }					\
} while (false)
#else
#define HEAPTRACKED(stmt) do { stmt; } while (false)
#endif
