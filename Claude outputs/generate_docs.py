"""
Generates three clearly-labeled SAMPLE/TEST documents for feeding into the
Tien_AgenticLoanPlatform's document-upload flow (record_document_upload),
for the fictional test applicant Jordan Rivera / APP-DECC702C.

These are deliberately NOT designed to visually resemble any real
government-issued ID, insurance card, or employer letterhead. They are
plain, obviously-labeled data sheets meant only to unblock the orchestrator's
"no documents on file" escalation check during local development testing.
"""

from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, HRFlowable
)
from reportlab.lib.enums import TA_CENTER

OUT_DIR = "/mnt/user-data/outputs/TestDocuments"

styles = getSampleStyleSheet()
title_style = ParagraphStyle(
    "DocTitle", parent=styles["Title"], fontSize=16, spaceAfter=6
)
label_style = ParagraphStyle(
    "Label", parent=styles["Normal"], textColor=colors.HexColor("#555555"),
    fontSize=9,
)
banner_style = ParagraphStyle(
    "Banner", parent=styles["Normal"], alignment=TA_CENTER, fontSize=10,
    textColor=colors.white, backColor=colors.HexColor("#B23A3A"),
    borderPadding=8, spaceAfter=18,
)
footer_style = ParagraphStyle(
    "Footer", parent=styles["Normal"], fontSize=8,
    textColor=colors.HexColor("#888888"), spaceBefore=24,
)

TEST_BANNER = (
    "SAMPLE / TEST DOCUMENT &mdash; FICTIONAL DATA GENERATED FOR SOFTWARE "
    "TESTING ONLY. NOT A REAL RECORD. NOT ISSUED BY ANY REAL PERSON, "
    "COMPANY, GOVERNMENT AGENCY, OR INSURER."
)
FOOTER_TEXT = (
    "Generated as a test fixture for the Tien_AgenticLoanPlatform "
    "orchestrator demo (application APP-DECC702C). This file contains no "
    "real personal information and does not represent any actual person, "
    "employer, insurer, or government record."
)


def make_doc(filename, title, rows, extra_paragraphs=None):
    path = f"{OUT_DIR}/{filename}"
    doc = SimpleDocTemplate(
        path, pagesize=letter,
        topMargin=0.75 * inch, bottomMargin=0.75 * inch,
        leftMargin=0.85 * inch, rightMargin=0.85 * inch,
    )
    story = []
    story.append(Paragraph(TEST_BANNER, banner_style))
    story.append(Paragraph(title, title_style))
    story.append(HRFlowable(width="100%", color=colors.HexColor("#CCCCCC")))
    story.append(Spacer(1, 14))

    if extra_paragraphs:
        for p in extra_paragraphs:
            story.append(Paragraph(p, styles["Normal"]))
            story.append(Spacer(1, 10))

    table = Table(rows, colWidths=[2.1 * inch, 4.0 * inch])
    table.setStyle(TableStyle([
        ("FONTNAME", (0, 0), (0, -1), "Helvetica-Bold"),
        ("FONTSIZE", (0, 0), (-1, -1), 10),
        ("TEXTCOLOR", (0, 0), (0, -1), colors.HexColor("#333333")),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 8),
        ("LINEBELOW", (0, 0), (-1, -1), 0.5, colors.HexColor("#DDDDDD")),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
    ]))
    story.append(table)
    story.append(Paragraph(FOOTER_TEXT, footer_style))
    doc.build(story)
    print(f"wrote {path}")


# ---------------------------------------------------------------------------
# 1. Employment & Income Verification Letter
# ---------------------------------------------------------------------------
make_doc(
    filename="income_verification_jordan_rivera.pdf",
    title="Employment &amp; Income Verification",
    extra_paragraphs=[
        "<b>Northgate Logistics &mdash; Human Resources (fictional test "
        "employer)</b>",
        "To Whom It May Concern: this letter confirms the employment and "
        "compensation details below for the applicant named, provided in "
        "connection with an auto loan application.",
    ],
    rows=[
        ["Applicant Name", "Jordan Rivera"],
        ["Employer", "Northgate Logistics (fictional test employer)"],
        ["Job Title", "Operations Manager"],
        ["Employment Status", "Full-time, active"],
        ["Employment Start Date", "March 2023 (~42 months)"],
        ["Gross Monthly Income", "$6,200.00"],
        ["Pay Frequency", "Semi-monthly"],
        ["Application Reference", "APP-DECC702C"],
        ["Letter Date", "2026-09-16"],
    ],
)

# ---------------------------------------------------------------------------
# 2. Proof of Auto Insurance
# ---------------------------------------------------------------------------
make_doc(
    filename="proof_of_insurance_jordan_rivera.pdf",
    title="Auto Insurance Declaration Summary",
    extra_paragraphs=[
        "<b>Lonestar Mutual Insurance (fictional test insurer)</b>",
        "Summary of active auto insurance coverage for the vehicle "
        "financed under the application referenced below.",
    ],
    rows=[
        ["Policyholder", "Jordan Rivera"],
        ["Policy Number", "TEST-POLICY-000123"],
        ["Vehicle", "2023 Toyota Camry"],
        ["VIN", "4T1BF1FK5NU123456"],
        ["Coverage Type", "Full coverage (liability, collision, comprehensive)"],
        ["Liability Limits", "$100,000 / $300,000 / $100,000"],
        ["Policy Effective", "2026-09-01"],
        ["Policy Expires", "2027-03-01"],
        ["Application Reference", "APP-DECC702C"],
    ],
)

# ---------------------------------------------------------------------------
# 3. Identity Verification Summary
# ---------------------------------------------------------------------------
make_doc(
    filename="identity_verification_summary_jordan_rivera.pdf",
    title="Identity Verification Summary",
    extra_paragraphs=[
        "This is a data summary of the automated identity verification "
        "check already performed by the platform at application submission "
        "(see identityCheck in the submit_application response) &mdash; "
        "not a copy of any government-issued identification document.",
    ],
    rows=[
        ["Applicant Name", "Jordan Rivera"],
        ["Date of Birth", "1990-05-14"],
        ["SSN (last 4)", "4321"],
        ["Address", "482 Maple Street, Austin, TX 78701"],
        ["Verification Provider", "LexisNexis (simulated)"],
        ["Verification ID", "LN-5B7742B5"],
        ["Address Match", "Yes"],
        ["SSN Match", "Yes"],
        ["DOB Match", "Yes"],
        ["Fraud Flag Raised", "No"],
        ["Application Reference", "APP-DECC702C"],
    ],
)

print("Done.")
