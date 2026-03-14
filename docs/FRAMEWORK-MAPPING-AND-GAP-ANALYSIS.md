# AI Safety Framework Mapping & Gap Analysis

**Date:** January 18, 2026  
**Purpose:** Reference guide for mapping MCP Benchmark capabilities to established AI safety frameworks

**Status:** Reference document - see [IMPLEMENTATION-ROADMAP.md](IMPLEMENTATION-ROADMAP.md) for current priorities

---

## Purpose

This document maps existing and planned capabilities to industry frameworks (OWASP, NIST, MITRE, ISO, EU AI Act). Use this as a **reference** when writing white papers, compliance reports, or responding to framework-specific questions.

**Not a strategy document** - for implementation priorities, see IMPLEMENTATION-ROADMAP.md

---

## Part 1: Current Capabilities Mapped to AI Safety Frameworks

### 1.1 OWASP Top 10 for LLM Applications (2023)

| OWASP Risk | MCP Benchmark Coverage | Implementation | Gap/Extension Needed |
|------------|------------------------|----------------|---------------------|
| **LLM01: Prompt Injection** | ⚠️ Partial | `PromptValidator` tests metadata compliance, not adversarial prompts | Add adversarial prompt testing framework |
| **LLM02: Insecure Output Handling** | ✅ Strong | `SchemaConfusion` attack, `ToolValidator` parameter validation | ✅ Already covered |
| **LLM03: Training Data Poisoning** | ❌ Not Applicable | MCP is runtime, not training | N/A for runtime governance |
| **LLM04: Model Denial of Service** | ✅ Strong | `PerformanceValidator` load testing, concurrency limits | Add resource exhaustion attack vectors |
| **LLM05: Supply Chain Vulnerabilities** | ⚠️ Partial | Transport validation, no dependency scanning | Add MCP server dependency analysis |
| **LLM06: Sensitive Information Disclosure** | ✅ Strong | `MetadataEnumeration` attack, `ContentSafetyAnalyzer` | Extend to PII/credential detection |
| **LLM07: Insecure Plugin Design** | ✅ **Industry-Leading** | `ToolValidator`, `SecurityValidator`, auth compliance | ✅ Core strength - document this! |
| **LLM08: Excessive Agency** | ⚠️ Foundational | Auth testing, scope validation | Add policy enforcement testing |
| **LLM09: Overreliance** | ❌ Not Covered | Human-in-the-loop validation not tested | Add approval workflow testing |
| **LLM10: Model Theft** | ⚠️ Partial | Auth enforcement | Add rate limiting bypass detection |

**Coverage Score:** 6.5/10 categories with strong/partial coverage  
**Unique Strength:** LLM07 (Insecure Plugin Design) - this is your differentiator

---

### 1.2 NIST AI Risk Management Framework (AI RMF 1.0)

| NIST Function | MCP Benchmark Mapping | Current Implementation | Enhancement Path |
|---------------|----------------------|------------------------|------------------|
| **GOVERN** | ⚠️ Foundational | Spec version tracking, rule registry | Add compliance report templates (SOC 2, ISO 42001) |
| **MAP** | ✅ Strong | Transport detection, capability enumeration, tool/resource/prompt discovery | Add risk classification taxonomy |
| **MEASURE** | ✅ **Industry-Leading** | Scoring strategies, performance metrics, vulnerability severity | Add reproducible benchmarks, leaderboards |
| **MANAGE** | ⚠️ Partial | Remediation guidance in reports | Add automated fix suggestions, policy templates |

**Key Insight:** Your `MEASURE` function is already production-grade - you have:
- Deterministic scoring (`PerformanceScoringStrategy`, `AggregateScoringStrategy`)
- Versioned baselines (spec profiles)
- Reproducible test harness

---

### 1.3 MITRE ATLAS (Adversarial Threat Landscape for AI Systems)

| ATLAS Tactic | MCP Benchmark Coverage | Attack Vectors Implemented | Missing Attack Vectors |
|--------------|------------------------|---------------------------|------------------------|
| **Reconnaissance** | ✅ Tested | `MetadataEnumeration` (AML.T0002) | Add timing attack detection |
| **Resource Development** | ❌ Not Covered | - | Out of scope for runtime testing |
| **Initial Access** | ✅ **Strong** | `McpCompliantAuthValidator` (AML.T0001) | Add credential stuffing tests |
| **Execution** | ✅ Strong | `ToolValidator`, parameter fuzzing | Add command injection specific tests |
| **Persistence** | ⚠️ Partial | Session handling validation | Add token refresh abuse testing |
| **Privilege Escalation** | ✅ Strong | Scope validation, auth bypass attempts | Add RBAC confusion tests |
| **Defense Evasion** | ✅ Strong | `JsonRpcErrorSmuggling`, schema confusion | Add protocol downgrade attacks |
| **Discovery** | ✅ Strong | Capability enumeration, tool/resource listing | ✅ Already comprehensive |
| **Impact** | ⚠️ Partial | DoS testing via load | Add data exfiltration detection |

**Coverage Score:** 7/9 tactics with strong/partial coverage  
**Unique Strength:** Defense Evasion testing (protocol-level attacks)

---

### 1.4 ISO/IEC 42001 AI Management System

| ISO 42001 Control Area | MCP Benchmark Alignment | Evidence | Gap |
|------------------------|------------------------|----------|-----|
| **6.1 Risk Assessment** | ✅ Strong | Vulnerability severity scoring, CVSS support | Add risk register export |
| **6.2 Risk Treatment** | ⚠️ Partial | Remediation guidance | Add treatment plan templates |
| **7.2 Documentation** | ✅ Strong | Markdown/HTML/JSON/XML reports, session logs | ✅ Already audit-ready |
| **8.2 Data Governance** | ⚠️ Partial | Content safety analysis | Add data lineage tracking |
| **8.3 AI System Lifecycle** | ✅ Strong | Versioned spec profiles, rule registry | Add CI/CD integration docs |
| **8.5 Transparency** | ✅ Strong | Detailed compliance reports, scoring breakdown | ✅ Already industry-leading |

---

### 1.5 EU AI Act (Regulation 2024/1689)

**Context:** Effective August 2024, with phased enforcement (General Purpose AI by Aug 2025, High-Risk AI by Aug 2026, All provisions by Aug 2027)

#### EU AI Act Risk Classification

Your tool primarily applies to **High-Risk AI Systems** (Annex III) when they use MCP for tool integration:

| Risk Category | Relevance to MCP Benchmark | Your Coverage |
|---------------|---------------------------|---------------|
| **Prohibited AI** (Art. 5) | Social scoring systems using AI tools | ⚠️ Partial - can detect enumeration |
| **High-Risk AI** (Art. 6, Annex III) | Employment, law enforcement, critical infrastructure | ✅ **Direct applicability** |
| **General Purpose AI** (Art. 51-55) | Foundation models with tool-use capabilities | ✅ Strong - protocol compliance |
| **Limited Risk** (Art. 50) | Chatbots, deepfakes | ⚠️ Partial - transparency testing |
| **Minimal Risk** | Everything else | ✅ Best practices validation |

#### MCP Benchmark Mapping to EU AI Act Requirements

##### For High-Risk AI Systems (Articles 8-15)

| EU AI Act Article | Requirement | MCP Benchmark Coverage | Implementation Status | Gap |
|-------------------|-------------|------------------------|----------------------|-----|
| **Art. 9 - Risk Management** | Document & mitigate foreseeable risks | ✅ **Strong** | Vulnerability severity scoring, risk categorization | Add continuous monitoring |
| **Art. 10 - Data Governance** | Ensure training/validation data quality | ⚠️ Partial | Content safety analysis | Add data provenance tracking |
| **Art. 11 - Technical Documentation** | Maintain comprehensive system docs | ✅ **Industry-Leading** | Multi-format reports, session logs, spec versioning | ✅ Compliant |
| **Art. 12 - Record-Keeping** | Automatic logging of events | ✅ **Strong** | Session logs, audit trails, performance metrics | Add retention policies |
| **Art. 13 - Transparency** | Information to users about AI use | ✅ Strong | Detailed compliance reports | Add user-facing summaries |
| **Art. 14 - Human Oversight** | Enable human intervention | ⚠️ Not Tested | - | Add human-override validation |
| **Art. 15 - Accuracy/Robustness** | Ensure reliable performance | ✅ **Strong** | Performance benchmarking, load testing, error handling | Add resilience testing |

##### For General Purpose AI Models (Articles 51-55)

| EU AI Act Article | Requirement | MCP Benchmark Coverage | Gap |
|-------------------|-------------|------------------------|-----|
| **Art. 53 - Transparency** | Technical documentation, training data info | ✅ Strong | Spec versioning, schema registry | Add model card generation |
| **Art. 53(1)(d) - Copyright** | Publicly available training data summary | ❌ Not Applicable | MCP is runtime, not training | N/A |
| **Art. 53(1)(g) - Testing** | Evaluation results & reports | ✅ **Industry-Leading** | Comprehensive test reports, scoring | ✅ Compliant |
| **Art. 54 - Systemic Risk** | Adversarial testing, cybersecurity | ✅ **Strong** | Attack vectors, auth compliance, protocol abuse testing | Add systemic risk assessment |

##### For Providers of High-Risk AI (Article 16)

| EU AI Act Article | Requirement | MCP Benchmark Coverage | Gap |
|-------------------|-------------|------------------------|-----|
| **Art. 16(a) - Quality Management** | Implement QMS per Art. 17 | ✅ Strong | Versioned specs, rule registry, CI/CD ready | Add QMS templates |
| **Art. 16(b) - Technical Documentation** | Draw up per Art. 11 | ✅ **Industry-Leading** | Automated report generation | ✅ Compliant |
| **Art. 16(c) - Logging** | Keep logs per Art. 12 | ✅ Strong | Session logs, audit trails | Add GDPR-compliant retention |
| **Art. 16(d) - Conformity Assessment** | Undergo assessment per Art. 43 | ✅ **Enables Compliance** | Your tool IS the assessment | ✅ Core value prop |
| **Art. 16(e) - CE Marking** | Affix CE marking | ❌ Not Applicable | Regulatory authority decision | Provide evidence package |
| **Art. 16(f) - Registration** | Register in EU database | ❌ Not Applicable | Provider responsibility | Provide registration data export |

#### EU AI Act Compliance Gaps & Opportunities

**What You Already Do (Compliance Enablers):**

✅ **Article 9 (Risk Management):** Vulnerability identification, severity scoring, CVSS support  
✅ **Article 11 (Technical Documentation):** Automated report generation, versioned schemas  
✅ **Article 12 (Record-Keeping):** Session logs, audit trails, performance metrics  
✅ **Article 15 (Accuracy/Robustness):** Load testing, error handling, performance benchmarks  
✅ **Article 43 (Conformity Assessment):** Your tool provides third-party assessment evidence  
✅ **Article 53 (GPAI Transparency):** Evaluation reports, test results, methodology documentation

**Critical Gaps for Full EU AI Act Alignment:**

| Gap Category | EU AI Act Article | What's Missing | Effort | Strategic Value |
|--------------|-------------------|----------------|---------|-----------------|
| **Human Oversight** | Art. 14 | Test that humans can override AI decisions | Medium (2-3 weeks) | Required for high-risk systems |
| **Data Governance** | Art. 10 | Data provenance, lineage tracking | High (4-6 weeks) | Enables GPAI compliance |
| **Continuous Monitoring** | Art. 9, 72 | Post-market monitoring, incident reporting | High (6-8 weeks) | Required for providers |
| **Fundamental Rights Impact** | Art. 27, Annex VII | Bias testing, fairness metrics | High (4-6 weeks) | Required for high-risk |
| **Conformity Assessment** | Art. 43, Annex VI | EU-compliant assessment procedures | Medium (3-4 weeks) | Enables notified body recognition |
| **CE Marking Evidence** | Art. 48-49 | Declaration of conformity template | Low (1 week) | Required for market placement |

#### EU AI Act Positioning Strategy

**Current State:**  
Your tool provides **conformity assessment evidence** for MCP-based AI systems

**Opportunity:**  
Become the **de facto conformity assessment tool** for AI systems using tool-calling/agentic patterns

**Path to EU AI Act Recognition:**

1. **Short-Term (Q1 2026):**
   - Add "EU AI Act Compliance Report" export format
   - Map findings to specific EU AI Act articles
   - Create Declaration of Conformity evidence package template
   - Document which requirements your tool validates

2. **Medium-Term (Q2-Q3 2026):**
   - Add missing validations (human oversight, fundamental rights)
   - Partner with EU Notified Bodies for assessment procedures
   - Contribute to EU AI Office standard-setting (Art. 40-41)
   - Get tool listed in EU AI regulatory sandbox (Art. 57)

3. **Long-Term (2027+):**
   - Become recognized assessment tool by EU AI Office
   - Get adopted by Notified Bodies (Art. 43) for conformity assessment
   - Influence harmonized standards (EN standards under Art. 40)
   - Enable "MCP Benchmark Certified = EU AI Act Compliant" claim

#### EU AI Act Timeline Alignment

| Date | EU AI Act Milestone | MCP Benchmark Action |
|------|---------------------|---------------------|
| **Feb 2025** | GPAI Code of Practice published | Map tool to Code of Practice requirements |
| **Aug 2025** | Prohibited AI & GPAI rules apply | Publish "EU AI Act for MCP Servers" guide |
| **Aug 2026** | High-Risk AI rules apply | Launch "EU AI Act Compliance Mode" in CLI |
| **Feb 2027** | Member State enforcement begins | Partner with 3+ EU Notified Bodies |
| **Aug 2027** | All provisions enforceable | Position as standard assessment tool |

#### Competitive Advantage

**Nobody else is doing EU AI Act-specific testing for agentic AI systems.**

Existing tools focus on:
- Model-level compliance (bias, fairness) - not runtime behavior
- GDPR/privacy - not AI-specific requirements
- General API security - not AI system risks

**Your unique position:**
- Test the **tool-calling layer** (where most high-risk AI operates)
- Provide **conformity assessment evidence** (Art. 43)
- Enable **technical documentation** (Art. 11)
- Validate **robustness** (Art. 15) at the integration layer

**Claim:** *"The first EU AI Act conformity assessment tool for agentic AI systems"*

---

## Summary: Framework Coverage

**Strong Coverage (Document & Maintain):**
- OWASP LLM07: Insecure Plugin Design
- NIST AI RMF: MEASURE function
- MITRE ATLAS: Defense Evasion tactics
- EU AI Act: Art. 9, 11, 12, 15, 53

**Gaps (Prioritize in Implementation):**
- OWASP: LLM01 (Prompt Injection), LLM09 (Overreliance)
- EU AI Act: Art. 10 (Data Governance), Art. 14 (Human Oversight), Art. 27 (Fundamental Rights)
- NIST: GOVERN and MANAGE functions

**See [IMPLEMENTATION-ROADMAP.md](IMPLEMENTATION-ROADMAP.md) for detailed gap-filling strategy.**
