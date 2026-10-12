# Star catalogue data: licence and attribution

`hyg41.bin.gz` is an adaptation of the **HYG Database, version 4.1**, by David Nash (astronexus):
<https://github.com/astronexus/HYG-Database>. HYG combines the Hipparcos, Yale Bright Star and Gliese
catalogues.

The HYG Database is licensed under the **Creative Commons Attribution-ShareAlike 4.0 International** licence
(CC BY-SA 4.0): <https://creativecommons.org/licenses/by-sa/4.0/>.

## Changes made

`tools/Hollow.CatalogBuilder` produced this file from `hygdata_v41.csv`. It:

- kept the systems within the distance where the catalogue still holds a share of their brightness level, at
  most 1,500 ly from Sol, or 3,000 ly for very luminous stars (Hollow Universe's GALAXY.md §8.3);
- converted positions to Hollow Universe's galactic frame, in light-years;
- turned spectral types into star classes, estimating missing ones from colour index and absolute magnitude,
  and corrected one known source error (Capella's spectral type);
- grouped multiple-star systems by primary, and placed companions with a missing or inconsistent distance next
  to their primary;
- built system names and search aliases from proper names, Bayer and Flamsteed designations, and the Gliese,
  HD, HIP and HR numbers;
- stored the result in a compact binary format ("HUSC" v1, gzip-compressed).

## Licence of this file

As an adaptation, `hyg41.bin.gz` is distributed under the same licence, **CC BY-SA 4.0**. You may share and adapt
it if you give the same attribution and license your changes under CC BY-SA 4.0.

This licence covers the catalogue data only. It does not cover the rest of Hollow Universe.
