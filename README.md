# HebrewBooks (HebrewBooks-2026)

תוכנת מדף־ספרים ("אוצריא"/HebrewBooks) לשולחן העבודה — WPF על ‏.NET 8‏, עם חיפוש
מלא (dtSearch), צפייה ב־PDF (PDF.js + PDFium), אינדוקס, ניהול מדפים, והורדת ספרים.

> **מקור הקוד** — הפרויקט המקורי (מאת *Moshe*, מזהה חבילה `HebrewBooks`) פורסם
> בעבר ל־`HebrewBooks-2026/Hebrewbooks-Releases` ואז נמחק. התוכנה מצהירה על עצמה
> כ־*"פרוייקט חינמי לזיכוי הרבים · כל הזכויות [לא שמורות]"*. **הקוד כאן שוחזר
> (decompiled) מתוך קובצי ה־`.nupkg`/`.dll` המותקנים** ולא מקוד־מקור מקורי, ואז
> נוקה עד שהוא מתקמפל מחדש. יוחסו לחשבון של Moshe זכויות היוצרים; ראו `NOTICE.md`.

## מבנה

```
src/
  HebrewBooks.Core            מודלים, ממשקים, לוגיקה בסיסית
  HebrewBooks.Data            SQLite/Dapper — קטלוג, מדפים, מועדפים
  HebrewBooks.Infrastructure  הגדרות, נתיבים, מערכת־הפעלה
  HebrewBooks.Search          מנוע החיפוש (dtSearch)
  HebrewBooks.Services        שירותים — הורדות, אינדוקס, TOC, עדכונים, לוח עברי
  HebrewBooks.Diagnostics     בדיקות אבחון
  HebrewBooks.Diagnostics.Ui  מסך אבחון (WPF)
  HebrewBooks.UI              היישום עצמו (WPF, מזהה assembly: HebrewBooks)
  hbsearch                    כלי CLI לאינדוקס/חיפוש
assets/runtime/               נכסי ריצה שאינם נבנים (PDF.js, qpdf, PDFium, DB, ICU)
lib/                          dtSearchNetApi4.dll (עטיפה מנוהלת)
```

## בנייה

צריך **‏.NET SDK 8‏** ו־Windows (WPF, x86). דאבל־קליק על **`בנה.bat`**, או:

```
dotnet build HebrewBooks.sln -c Release
```

## הרצה מקומית

```
dotnet run --project src/HebrewBooks.UI -c Release
```

## פרסום Release

**`שחרר.bat`** בונה חבילת Velopack ומעלה אותה ל־GitHub Releases של
`yossi-computers/HebrewBooks-2026` (ערוץ `stable`). היישום המותקן מתעדכן משם
אוטומטית. צריך `gh auth login` פעם אחת.

```
שחרר.bat 3.0.120 "מה השתנה"                (release ציבורי)
שחרר.bat 3.0.120 "מה השתנה" -prerelease    (התקנה ידנית בלבד)
שחרר.bat 3.0.120 -draft                    (טיוטה, רק לך)
```

## גיבוי לקוד־המקור

**`גבה.bat`** עושה commit ו־push של התיקייה לענף הנוכחי.

## הערות ריצה / נכסים כבדים

חלק מנכסי הריצה גדולים מדי ל־git ולכן **אינם** בריפו (מסומנים ב־`.gitignore`):
`cite.db`, `synonyms.db`, `icudt63.dll`, `qpdf/`, `x86/pdfium`. `בנה.bat -publish`
ו־`שחרר.bat` משחזרים אותם אוטומטית מעותק מותקן ב־
`%LOCALAPPDATA%\HebrewBooks\current`.

## מה שונה מהמקור

- ערוץ העדכונים והמאגר הופנו מ־`HebrewBooks-2026/Hebrewbooks-Releases` (נמחק)
  אל `yossi-computers/HebrewBooks-2026` (`AppUpdateService`, `hbsearch` Options).
- כתובות של קובצי־נתונים מקדימים (`cite.db`, `synonyms.db`, `HebAram.DB` וכו')
  עדיין מצביעות על מאגר ה־prerequisites המקורי שנמחק — הן **לא פעילות**. הקבצים
  נשלחים עם ההתקנה, כך שהתוכנה עובדת, אבל הורדה מרחוק שלהם תיכשל בשקט עד שתארחו
  אותם מחדש. זה נשאר כ־TODO להחלטתכם.

## רישוי

ראו `NOTICE.md`. אין קובץ רישיון פורמלי במקור; ההצהרה "כל הזכויות לא שמורות"
מרמזת על שחרור, אך פרסום מחדש הוא באחריותכם ובכפוף לזכויות של המחבר המקורי.
