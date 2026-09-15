# Slab support-count envelope

Both selectors offer 2, 3, 4 and 5+. Counts 2–4 use their exact continuous-beam model. For 5+, reaction, absolute shear, absolute moment and span deflection each use the larger result from five and six supports. Different responses may govern in different cases.

## Applicability

The envelope assumes equal spans, constant EI, full uniform loading and equal cantilevers at both ends. It does not cover unequal spans, partial loading, variable stiffness or asymmetric cantilevers. It is a derived analysis model, not a Doka published coefficient table.

The diagram is explicitly labelled as a five-support illustration. Its individual reactions need not equal the governing envelope output. Five supports must fit the selected H20; the UI also shows the maximum count fitting the selected length, spacing and cantilevers. The 5+ selection does not authorize an arbitrarily long layout on that member.

## Why five and six cover higher counts

Let N be the number of supports, m=N−1, L the span, a the equal end cantilever, q the line load and r=−2+sqrt(3). The three-moment equation gives support moments

    M_i = −qL²/12 + (−qa²/2 + qL²/12) f_i
    f_i = (r^i + r^(m−i)) / (1+r^m),  i=0…m.

For every N≥5, each adjacent pair (x,y)=(f_i,f_(i+1)) lies in the polygon

    −2/7 ≤ x,y ≤ 1
    −4/19 ≤ x+y ≤ 14/19
    4x+y ≥ −1
    x+4y ≥ −1.

These bounds follow from the alternating powers of r with |r|<1 and the recurrence f_(i−1)+4f_i+f_(i+1)=0. All polygon vertices occur in the five- or six-support cases:

    (−2/7,1/7), (−5/19,1/19), (1/19,−5/19), (1/7,−2/7),
    (1,−2/7), (1,−5/19), (−5/19,1), (−2/7,1).

At any position in a span, moment, shear and displacement are affine functions of its two end moments. Their maximum absolute values are convex functions, so their maxima over this polygon occur at its vertices. This proves that taking the component-wise maximum from five and six supports bounds those responses for all N≥5.

Interior support reactions are qL/2−6M_i/L; interior f_i ranges from −2/7 to 1/7, both attained with five supports. End reactions are qL/2+(M_1−M_0)/L+qa; the extreme first-interior coefficients −2/7 and −5/19 occur with five and six supports. Thus these cases also cover both the maximum and minimum reaction. The minimum is retained for the uplift check; the warning identifies its support and case count. Cantilever shear qa and root moment qa²/2 are independent of support count.

For zero cantilever, maximum reaction divided by qL is 1.1 for four supports, 8/7 for five and 43/38 for six. Four supports therefore cannot stand in for all higher counts.

## Implementation and verification

ContinuousBeamResponse solves the tridiagonal three-moment system and calculates reactions, shear and moments. ContinuousSpanDeflection finds the slope roots of the integrated span displacement. SlabSupportEnvelope combines the five- and six-support responses separately for each criterion. Spacing is evaluated directly on the 25 mm grid.

Tests compare the solver with closed-form moments, force equilibrium and independent polynomial displacement evaluations through 100 supports over nine cantilever ratios. Polygon inequalities are checked through 256 supports. These numerical checks supplement the envelope derivation; they are not the reason for truncating the calculation to five and six.

Uplift now invalidates the layout: primary geometry is checked once, and secondary contact is checked at each otherwise passing spacing candidate. Reactions below −1e-10 times the maximum absolute reaction are rejected; this tolerance handles numerical cancellation at zero without clipping negative reactions. Tests include the three-support formula R_middle/q=1.25L−1.5a²/L, boundary perturbations, load scaling, higher-count minima and UI invalidation/recovery.

Cantilever-tip deflection including support rotation and the remaining complete formwork load-path checks remain deferred audit items. This change checks span deflection and retains the existing cantilever-tip precheck. Actual Revit transactions were not exercised by the test host.
