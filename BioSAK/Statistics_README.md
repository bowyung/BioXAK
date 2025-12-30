# Statistical Analysis Methods - BioSAK GraphGen

This document explains the statistical methods implemented in the BioSAK GraphGen statistical analysis module.

---

## Table of Contents
1. [T-Test](#t-test)
2. [One-Way ANOVA](#one-way-anova)
3. [Two-Way ANOVA](#two-way-anova)
4. [Post-hoc Tests](#post-hoc-tests)
5. [Decision Logic Summary](#decision-logic-summary)

---

## T-Test

### Overview
The t-test compares means between two groups to determine if they are statistically different.

### Available Options

#### 1. Comparison Mode
- **All Pairs**: Automatically computes t-tests for all possible pairwise combinations of selected groups
- **Select Two**: Manually select exactly two groups to compare

#### 2. Test Type Selection

**Paired vs Independent:**
- **Paired t-test**: Use when samples are related (e.g., before/after measurements on the same subjects)
  - Requires equal sample sizes in both groups
  - Calculates differences between paired observations
  - Formula: t = (mean of differences) / (SE of differences)
  
- **Independent t-test**: Use when samples are from different, unrelated groups

**Variance Equality (for Independent t-test only):**

When "Auto-detect variance equality" is checked:
1. **F-test** is performed to test H₀: σ₁² = σ₂²
2. If F-test p > 0.05: **Student's t-test** (assumes equal variances)
   - Uses pooled variance estimate
   - df = n₁ + n₂ - 2
3. If F-test p ≤ 0.05: **Welch's t-test** (does not assume equal variances)
   - Uses Welch-Satterthwaite degrees of freedom approximation
   - More robust for unequal variances and sample sizes

**Default Behavior:**
- If F-test cannot be computed (e.g., zero variance): defaults to **Welch's t-test**

#### 3. Tail Selection
- **Two-tailed**: Tests H₀: μ₁ = μ₂ vs H₁: μ₁ ≠ μ₂
- **One-tailed (>)**: Tests H₀: μ₁ ≤ μ₂ vs H₁: μ₁ > μ₂
- **One-tailed (<)**: Tests H₀: μ₁ ≥ μ₂ vs H₁: μ₁ < μ₂

For one-tailed tests, the p-value is halved in the direction of the alternative hypothesis.

---

## One-Way ANOVA

### Overview
One-Way ANOVA tests whether there are statistically significant differences among three or more groups.

### Variance Homogeneity Testing

**Levene's Test** is automatically performed:
- Tests H₀: All group variances are equal
- Uses absolute deviations from group medians (more robust than using means)

### ANOVA Type Selection

Based on Levene's test result:

| Levene's p-value | ANOVA Type | Assumption |
|------------------|------------|------------|
| > 0.05 | Standard One-Way ANOVA | Equal variances |
| ≤ 0.05 | Welch's ANOVA | Unequal variances |

**Welch's ANOVA:**
- Does not assume equal variances
- Uses weighted means and adjusted degrees of freedom
- More robust for heteroscedastic data

**Default Behavior:**
- If Levene's test cannot be computed: defaults to **Standard ANOVA** (assumes equal variances)

### Post-hoc Tests

Post-hoc tests are performed only if:
1. Post-hoc option is checked
2. ANOVA p-value < 0.05

**Method Selection (automatic based on Levene's test):**

| Levene's p-value | Post-hoc Method |
|------------------|-----------------|
| > 0.05 | Tukey's HSD |
| ≤ 0.05 | Games-Howell |

**Tukey's HSD (Honestly Significant Difference):**
- Assumes equal variances
- Controls family-wise error rate
- Uses studentized range distribution

**Games-Howell:**
- Does not assume equal variances
- Uses Welch-Satterthwaite degrees of freedom
- Appropriate for unequal sample sizes and variances

---

## Two-Way ANOVA

### Overview
Two-Way ANOVA examines the effects of two independent variables (factors) and their interaction.

- **Factor A**: Series (groups)
- **Factor B**: X values (conditions)
- **Interaction (A × B)**: Whether the effect of one factor depends on the level of the other

### Options

#### 1. Include Interaction Term
- **Checked (default)**: Tests main effects A, B, and interaction A×B
- **Unchecked**: Tests only main effects A and B

#### 2. Sum of Squares Type

| Type | Name | Description | When to Use |
|------|------|-------------|-------------|
| Type I | Sequential | Effects tested in order; each adjusted for previous | Balanced designs with ordered factors |
| Type II | Hierarchical | Each main effect adjusted for other main effects only | No interaction expected |
| Type III | Marginal (default) | Each effect adjusted for all other effects | Unbalanced designs, general use |

**Automatic Selection:**
- For unbalanced designs, Type III is always used regardless of selection

### Post-hoc Analysis

**Based on Interaction Significance:**

| Interaction p-value | Analysis Type | Description |
|---------------------|---------------|-------------|
| < 0.05 | Simple Main Effects | Tests effect of each factor at each level of the other factor |
| ≥ 0.05 | Main Effects Tests | Pairwise comparisons for significant main effects |

**Simple Main Effects:**
When interaction is significant, the effect of one factor depends on the other. Therefore:
- Effect of Factor A is tested separately at each level of Factor B
- Effect of Factor B is tested separately at each level of Factor A

**Main Effects Tests:**
When interaction is not significant:
- If Factor A is significant: Pairwise comparisons between series
- If Factor B is significant: Pairwise comparisons between X values

---

## Post-hoc Tests

### Tukey's HSD
```
q = |Mean₁ - Mean₂| / SE

where SE = √(MSE × (1/n₁ + 1/n₂) / 2)
```
- Uses studentized range distribution
- Controls Type I error across all comparisons

### Games-Howell
```
t = |Mean₁ - Mean₂| / SE

where SE = √(s₁²/n₁ + s₂²/n₂)
df = Welch-Satterthwaite approximation
```
- Does not assume equal variances
- Adjusts degrees of freedom for each comparison

---

## Decision Logic Summary

### T-Test Decision Tree
```
Is comparison paired?
├─ YES → Paired t-test
└─ NO → Independent t-test
         ├─ Can F-test be computed?
         │   ├─ YES → F-test p > 0.05?
         │   │        ├─ YES → Student's t-test
         │   │        └─ NO → Welch's t-test
         │   └─ NO → Welch's t-test (default)
         └─ Apply tail correction if one-tailed
```

### One-Way ANOVA Decision Tree
```
Perform Levene's test for variance homogeneity
├─ Can Levene's test be computed?
│   ├─ YES → Levene's p > 0.05?
│   │        ├─ YES → Standard ANOVA, Tukey's HSD
│   │        └─ NO → Welch's ANOVA, Games-Howell
│   └─ NO → Standard ANOVA (default)
└─ ANOVA p < 0.05?
    ├─ YES → Perform selected post-hoc test
    └─ NO → No post-hoc (no significant differences)
```

### Two-Way ANOVA Decision Tree
```
Check design balance
├─ Balanced → Use selected SS Type
└─ Unbalanced → Force Type III

Include interaction?
├─ YES → Test A, B, and A×B
│        └─ Interaction p < 0.05?
│            ├─ YES → Simple Main Effects
│            └─ NO → Main Effects Tests
└─ NO → Test A and B only
         └─ Main Effects Tests for significant factors
```

---

## Statistical Formulas

### F-test for Variance Equality
```
F = s₁² / s₂²  (larger / smaller)
df₁ = n₁ - 1, df₂ = n₂ - 1
```

### Levene's Test
```
1. Calculate |Xᵢⱼ - Median(Xⱼ)| for each observation
2. Perform ANOVA on absolute deviations
```

### Welch-Satterthwaite Degrees of Freedom
```
df = (s₁²/n₁ + s₂²/n₂)² / [(s₁²/n₁)²/(n₁-1) + (s₂²/n₂)²/(n₂-1)]
```

### Welch's ANOVA
```
F = Σwᵢ(X̄ᵢ - X̄ᵥᵥ)² / (k-1) / [1 + 2(k-2)λ/(k²-1)]

where:
  wᵢ = nᵢ / sᵢ²
  X̄ᵥᵥ = Σwᵢ X̄ᵢ / Σwᵢ
  λ = 3Σ[(1 - wᵢ/Σwᵢ)²/(nᵢ-1)] / (k²-1)
```

---

## Significance Levels

| p-value | Significance | Symbol |
|---------|--------------|--------|
| p < 0.001 | Highly significant | *** |
| p < 0.01 | Very significant | ** |
| p < 0.05 | Significant | * |
| p ≥ 0.05 | Not significant | ns |

---

## References

1. Welch, B. L. (1947). The generalization of "Student's" problem when several different population variances are involved. Biometrika, 34(1/2), 28-35.

2. Games, P. A., & Howell, J. F. (1976). Pairwise multiple comparison procedures with unequal n's and/or variances: a Monte Carlo study. Journal of Educational Statistics, 1(2), 113-125.

3. Levene, H. (1960). Robust tests for equality of variances. In Contributions to Probability and Statistics (pp. 278-292).

4. Brown, M. B., & Forsythe, A. B. (1974). The small sample behavior of some statistics which test the equality of several means. Technometrics, 16(1), 129-132.

---

*BioSAK GraphGen Statistical Module - Version 1.0*
