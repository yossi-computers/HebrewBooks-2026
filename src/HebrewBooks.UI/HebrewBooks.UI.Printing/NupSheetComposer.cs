using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HebrewBooks.UI.Printing;

public static class NupSheetComposer
{
	public static FixedDocument BuildDocument(IReadOnlyList<BitmapSource> pages, Size sheetSize, NupPrintSettings settings)
	{
		FixedDocument fixedDocument = new FixedDocument();
		fixedDocument.DocumentPaginator.PageSize = sheetSize;
		int num = ((settings.PagesPerSheet < 1) ? 1 : settings.PagesPerSheet);
		if (num == 1)
		{
			foreach (BitmapSource page in pages)
			{
				FixedPage fixedPage = new FixedPage
				{
					Width = sheetSize.Width,
					Height = sheetSize.Height
				};
				Image element = new Image
				{
					Source = page,
					Stretch = Stretch.Uniform,
					Width = sheetSize.Width,
					Height = sheetSize.Height
				};
				FixedPage.SetLeft(element, 0.0);
				FixedPage.SetTop(element, 0.0);
				fixedPage.Children.Add(element);
				fixedDocument.Pages.Add(Wrap(fixedPage));
			}
			return fixedDocument;
		}
		(int cols, int rows) tuple = GridFor(num);
		int item = tuple.cols;
		int item2 = tuple.rows;
		double margin = settings.Margin;
		double gutter = settings.Gutter;
		double num2 = (sheetSize.Width - 2.0 * margin - (double)(item - 1) * gutter) / (double)item;
		double num3 = (sheetSize.Height - 2.0 * margin - (double)(item2 - 1) * gutter) / (double)item2;
		for (int i = 0; i < pages.Count; i += num)
		{
			FixedPage fixedPage2 = new FixedPage
			{
				Width = sheetSize.Width,
				Height = sheetSize.Height
			};
			for (int j = 0; j < num && i + j < pages.Count; j++)
			{
				int num4;
				int num5;
				switch (settings.Order)
				{
				case NupOrderMode.LeftToRightThenDown:
					num4 = j / item;
					num5 = j % item;
					break;
				case NupOrderMode.TopToBottomThenLeft:
					num4 = j % item2;
					num5 = item - 1 - j / item2;
					break;
				case NupOrderMode.TopToBottomThenRight:
					num4 = j % item2;
					num5 = j / item2;
					break;
				default:
					num4 = j / item;
					num5 = item - 1 - j % item;
					break;
				}
				double length = margin + (double)num5 * (num2 + gutter);
				double length2 = margin + (double)num4 * (num3 + gutter);
				Image element2 = new Image
				{
					Source = pages[i + j],
					Stretch = Stretch.Uniform,
					Width = num2,
					Height = num3
				};
				FixedPage.SetLeft(element2, length);
				FixedPage.SetTop(element2, length2);
				fixedPage2.Children.Add(element2);
			}
			fixedDocument.Pages.Add(Wrap(fixedPage2));
		}
		return fixedDocument;
	}

	private static PageContent Wrap(FixedPage fp)
	{
		PageContent pageContent = new PageContent();
		((IAddChild)pageContent).AddChild((object)fp);
		return pageContent;
	}

	private static (int cols, int rows) GridFor(int n)
	{
		return n switch
		{
			2 => (cols: 2, rows: 1), 
			4 => (cols: 2, rows: 2), 
			6 => (cols: 2, rows: 3), 
			8 => (cols: 2, rows: 4), 
			9 => (cols: 3, rows: 3), 
			_ => Sqrtish(n), 
		};
	}

	private static (int cols, int rows) Sqrtish(int n)
	{
		int num = (int)Math.Ceiling(Math.Sqrt(n));
		int item = (int)Math.Ceiling((double)n / (double)num);
		return (cols: num, rows: item);
	}
}
