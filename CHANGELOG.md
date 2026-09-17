# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- `BoolOpsEngine` with `Execute`, `Simplify` and `UnionAll`, and the `BoolOps` module with `union`, `intersection`, `difference`, `xor`, `simplify`, `unionAll` and their `...With` tolerance variants.
- The pipeline of DESIGN.md: ingest, intersect with the segment tree, split, cluster, graph, winding by propagation seeded with one ray cast per component, select and link.
- `Bvh.VisitClosePairsWith`: a dual tree traversal between two trees.
- Engine tests against a point in region oracle and area identities on random, self intersecting and degenerate input.

## [0.0.1] - 2026-09-17
### Added
- Project scaffold: `FillRule`, `ClipType` and `Shape` public types, the internal growable buffers, sorting helpers and the float array BVH. No boolean operations yet, see `DESIGN.md`.
