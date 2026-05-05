---
sidebar_position: 6
---

# Validation

NetMediate does not include built-in validation. Implement validation as a pipeline behavior.

For a complete validation example, see [VALIDATION_BEHAVIOR_SAMPLE.md](https://github.com/schivei/net-mediate/blob/main/docs/VALIDATION_BEHAVIOR_SAMPLE.md) in the repository.

## Recommended Approach

Use FluentValidation or your preferred validation library and implement it as a pipeline behavior to validate messages before they reach handlers.
