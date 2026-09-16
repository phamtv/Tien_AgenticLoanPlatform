"""
Regenerates the income-verification and identity documents so their layout
actually matches what DocumentExtractionSchemas.cs asks Claude to extract
(pay_stub and id_document schemas respectively) — the earlier generic
verification-letter versions didn't contain those specific fields, so
extraction correctly came back mostly null.

Still clearly labeled SAMPLE/TEST/fictional data throughout.
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
title_style = ParagraphStyle("DocTitle", parent=styles["Title"], fontSize=16, spaceAfter=6)
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
    "COMPANY, OR GOVERNMENT AGENCY."
)
FOOTER_TEXT = (
    "Generated as a test fixture for the Tien_AgenticLoanPlatform "
    "orchestrator demo (application APP-DECC702C). Fictional data only."
)


def make_doc(filename, title, rows, subtitle=None):
    path = f"{OUT_DIR}/{filename}"
    doc = SimpleDocTemplate(
        path, pagesize=letter,
        topMargin=0.75 * inch, bottomMargin=0.75 * inch,
        leftMargin=0.85 * inch, rightMargin=0.85 * inch,
    )
    story = [Paragraph(TEST_BANNER, banner_style), Paragraph(title, title_style)]
    if subtitle:
        story.append(Paragraph(subtitle, styles["Normal"]))
        story.append(Spacer(1, 8))
    story.append(HRFlowable(width="100%", color=colors.HexColor("#CCCCCC")))
    story.append(Spacer(1, 14))

    table = Table(rows, colWidths=[2.3 * inch, 3.8 * inch])
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
# pay_stub schema: employeeName, employerName, payPeriodStart, payPeriodEnd,
# payDate, grossPayCurrent, grossPayYtd, netPayCurrent, payFrequency
# ---------------------------------------------------------------------------
make_doc(
    filename="income_verification_jordan_rivera.pdf",
    title="Northgate Logistics &mdash; Earnings Statement",
    subtitle="(fictional test employer)",
    rows=[
        ["Employee Name", "Jordan Rivera"],
        ["Employer Name", "Northgate Logistics"],
        ["Pay Period Start", "2026-08-16"],
        ["Pay Period End", "2026-08-31"],
        ["Pay Date", "2026-09-01"],
        ["Gross Pay (this period)", "$3,100.00"],
        ["Gross Pay (year to date)", "$49,600.00"],
        ["Net Pay (this period)", "$2,325.15"],
        ["Pay Frequency", "Semimonthly"],
        ["Application Reference", "APP-DECC702C"],
    ],
)

# ---------------------------------------------------------------------------
# id_document schema: fullName, dateOfBirth, documentNumber,
# issuingStateOrCountry, expirationDate, address
# ---------------------------------------------------------------------------
make_doc(
    filename="identity_verification_summary_jordan_rivera.pdf",
    title="State of Texas &mdash; Sample Identification Card",
    subtitle="(fictional test ID &mdash; not a real government-issued document; no photo, seal, or security features included)",
    rows=[
        ["Full Name", "Jordan Rivera"],
        ["Date of Birth", "1990-05-14"],
        ["Document Number", "TX-TEST-00998877"],
        ["Issuing State", "Texas"],
        ["Expiration Date", "2031-05-14"],
        ["Address", "482 Maple Street, Austin, TX 78701"],
        ["Application Reference", "APP-DECC702C"],
    ],
)

print("Done.")
