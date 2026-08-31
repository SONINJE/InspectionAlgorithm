#define NOMINMAX
#include <windows.h>
#include <cstdlib>
#include <algorithm>
#include <math.h>
#include "fchain.h"



CChain::CChain(int nMaxChain, long nMaxChainData )
{
    m_chain     = new int[nMaxChain]; // 최대로 구할 Chain 수 
	m_chainstart= new long[nMaxChain];  // 
	m_chaindata = new int[2*nMaxChainData];  // 구한 Chain의 길이의 합이 MAXCHAINDATA를 넘을수 없다.
    MAXCHAIN     = nMaxChain;
	MAXCHAINDATA = nMaxChainData;
	FLAG_SetData = FALSE;
}

CChain::~CChain()
{
    delete m_chain;
	delete m_chainstart;
	delete m_chaindata;
}

void CChain::SetChainData(int Object, LPBYTE fm, int skipx, int skipy, int minboundary,
						  long maxboundary, int width, int height)
{
	 if(Object==0) { OBJECT=LOW  ; BACKGROUND = HIGH; }
	 else          { OBJECT=HIGH ; BACKGROUND = LOW ; }
	 m_fm          = fm;          // 메모리데이타의 시작번지 
     m_skipx	   = skipx;       // x방향 skip 픽셀수 
     m_skipy	   = skipy;       // y방향 skip 픽셀수 
	 m_minboundary = minboundary; // 무시할 chain의 길이 (이값이하인 chain 은 무시)
     m_maxboundary = maxboundary; // 무시할 chain의 길이 (이값이상인 chain 은 무시)
	 WIDTH_MEM     = width;       // 영상의 x 방향 폭
	 imageheight  = height;      // 영상의 y 방향 폭
	 FLAG_SetData  = TRUE;
}


int CChain::FastChain(int x1, int y1, int x2, int y2)
{
	 int i,j,k;
	 int dum;
	 int blackpoint=0, totaldatanumber=0;
          
     if(!FLAG_SetData) return -1; // Data Setting이 안되었으면 -1을 return 한다. 

	 if(x1>x2) { dum=x1; x1=x2; x2=dum ; }
	 if(y1>y2) { dum=y1; y1=y2; y2=dum ; }
	 if(x1<0 || y1 <0 ) return -2;
	 if(x2>=WIDTH_MEM)    x2=WIDTH_MEM-1;
	 if(y2>=imageheight) y2=imageheight-1;

	 // initialize 
	 if(m_skipx<1) m_skipx=1;              // Skip하는 픽셀이 2보다 작을수 없다.
	 if(m_skipy<1) m_skipy=1;              //
	 if(m_minboundary<2) m_minboundary=2;  // 구한 chain의 길이가 5보다 작을수 없다.
	 
	 m_chainCount=0;

     //BEGIN-0 : 윈도우 Line을 먼저 배경으로 채운다. >>>>>>>>>>>>>>>>>>>>>>>>>>>>
	 for(i=x1;i<=x2;i++)                   
	 {    
		  *(m_fm+WIDTH_MEM*y1+i)=BACKGROUND;
		  *(m_fm+WIDTH_MEM*y2+i)=BACKGROUND;  
	 }
	 for(i=y1;i<=y2;i++)
	 {  
		  *(m_fm+WIDTH_MEM*i+x1)=BACKGROUND;
		  *(m_fm+WIDTH_MEM*i+x2)=BACKGROUND; 
	 }
	 //END-0 <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<

	 //scan and find boundary pixel 
	 for(i=y1; i<=y2 ;i+=m_skipy)
		 for(j=x1; j<=x2 ;j+=m_skipx)
			 if(*(m_fm+i*WIDTH_MEM+j)==OBJECT &&
						  *(m_fm+i*WIDTH_MEM+j+1)==OBJECT ) // because of noise 
			 {
				 blackpoint=0;
				 for(k=j;k>=(j-m_skipx);k--)
					if(*(m_fm+i*WIDTH_MEM+k) == BACKGROUND )
					{   blackpoint=k+1;  break;   } // (blackpoint,i) is boundary pixel 
				 
				 // if this object didnt be checked yet
				 if( *(m_fm+i*WIDTH_MEM+blackpoint)!=BOUNDARY && blackpoint!=0)
				 {
					 m_chain[m_chainCount]=FindBoundaryPixel(blackpoint,i,
								&m_chaindata[2*totaldatanumber], MAXCHAINDATA-totaldatanumber);
					 if(m_chain[m_chainCount]>=m_minboundary)
					 {
						  totaldatanumber+=m_chain[m_chainCount];
						  m_chainCount++;
					 }
					 
					 //  if totaldatanumber is greater than MAXCHAINDATA or   
					 //  if m_chainCount is greater than MAXCHAIN--> STOP finding 
					 else if(m_chain[m_chainCount]==-1) goto END;
					 if(m_chainCount>=MAXCHAIN) goto END;
				 }
			 }
	 
END: totaldatanumber=0;
	 for(i=0;i<m_chainCount;i++)
     {
		 m_chainstart[i]=2*totaldatanumber;
         totaldatanumber+=m_chain[i];
	 }

	 return m_chainCount;

}

int	CChain::GetChainNum()
{
	return m_chainCount;
}

int  CChain::FindBoundaryPixel(int startx, int starty,int *data, long emptyarray)
{
	  long count=0;
	  int cx,cy;
	  int bcx,bcy;
	  LPBYTE cfm;
	  int SP;   // start point 

	  if(emptyarray<0) return -1;

	  cx=bcx=startx; cy=bcy=starty;

	  data[0]=cx; data[1]=cy; count++; SP=3;

	  cfm=m_fm+cy*WIDTH_MEM+cx;

	  do
	  {
		  switch(SP)
		  {
				case 1: if( *(cfm-WIDTH_MEM-1)==OBJECT ||*(cfm-WIDTH_MEM-1)==BOUNDARY)
						  {
							  cx=cx-1; cy=cy-1;
							  SP=7;
							  break;
						  }
				case 2: if( *(cfm-1)==OBJECT || *(cfm-1)==BOUNDARY)
						  {
							  cx=cx-1; cy=cy;
							  SP=1;
							  break;
						  }
				case 3: if( *(cfm+WIDTH_MEM-1)==OBJECT || *(cfm+WIDTH_MEM-1)==BOUNDARY)
						  {
							  cx=cx-1; cy=cy+1;
							  SP=1;
							  break;
						  }
				case 4: if( *(cfm+WIDTH_MEM)==OBJECT || *(cfm+WIDTH_MEM)==BOUNDARY)
						  {
							  cx=cx; cy=cy+1;
							  SP=3;
							  break;
						  }
				case 5: if( *(cfm+WIDTH_MEM+1)==OBJECT || *(cfm+WIDTH_MEM+1)==BOUNDARY)
						  {
							  cx=cx+1; cy=cy+1;
							  SP=3;
							  break;
						  }
				case 6: if( *(cfm+1)==OBJECT || *(cfm+1)==BOUNDARY)
						  {
							  cx=cx+1; cy=cy;
							  SP=5;
							  break;
						  }
				case 7: if( *(cfm-WIDTH_MEM+1)==OBJECT || *(cfm-WIDTH_MEM+1)==BOUNDARY)
						  {
							  cx=cx+1; cy=cy-1;
							  SP=5;
							  break;
						  }
				case 8: if( *(cfm-WIDTH_MEM)==OBJECT || *(cfm-WIDTH_MEM)==BOUNDARY)
						  {
							  cx=cx; cy=cy-1;
							  SP=7;
							  break;
						  }

				default:if( *(cfm-WIDTH_MEM-1)==OBJECT || *(cfm-WIDTH_MEM-1)==BOUNDARY)
						  {
							  cx=cx-1; cy=cy-1;
							  SP=7;
							  break;
						  }
						  else if( *(cfm-1)==OBJECT || *(cfm-1)==BOUNDARY)
						  {
							  cx=cx-1; cy=cy;
							  SP=1;
							  break;
						  }
						  else if( *(cfm+WIDTH_MEM-1)==OBJECT || *(cfm+WIDTH_MEM-1)==BOUNDARY)
						  {
							  cx=cx-1; cy=cy+1;
							  SP=1;
							  break;
						  }
						  else if( *(cfm+WIDTH_MEM)==OBJECT || *(cfm+WIDTH_MEM)==BOUNDARY)
						  {
							  cx=cx; cy=cy+1;
							  SP=3;
							  break;
						  }
						  else if( *(cfm+WIDTH_MEM+1)==OBJECT || *(cfm+WIDTH_MEM+1)==BOUNDARY)
						  {
							  cx=cx+1; cy=cy+1;
							  SP=3;
							  break;
						  }

		  } 
		  if(count >= emptyarray)
			  return -1;  // 기 절정한 최대 메모리를 초과하면 -1을 돌려줌
		  data[2*count]  = cx;
		  data[2*count+1]= cy;
		  count++;
		  cfm=m_fm+cy*WIDTH_MEM+cx;
		  *(cfm)=BOUNDARY;
		}while( cx!=startx || cy!=starty);
		if(--count<m_minboundary) return 0;   //구한 boundary가 m_minboundary보다 작거나 
		else if(count>m_maxboundary) return 0;//				m_maxboundary보다 크면 0을 돌려준다.
		else                         return count;
}

// Find Center with only boundary pixels ---------------------------------
int CChain::FindCenterWithBoundary(int N, double *cx, double *cy)
{
	 int i;
	 double sumx,sumy;
	 int Number;
     int *BPixel;      

	 Number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 sumx=sumy=0;
	 for(i=0;i<Number;i++)
	 {
			  sumx+= BPixel[2*i];;
			  sumy+= BPixel[2*i+1];
	 }

	 *cx = sumx / Number;
	 *cy = sumy / Number;

	 return 0;
}




//  Boundary 바깥의 짤려진 부분을 보상하기 위한 테이블
BYTE L[9][9]={ {0,0,0,0,0,0,0,0,0},
			   {0,4,5,6,7,8,0,2,3},{0,3,4,5,6,7,8,0,0},{0,2,3,4,5,6,7,8,0},
			   {0,0,0,3,4,5,6,7,8},{0,8,0,2,3,4,5,6,7},{0,7,8,0,0,3,4,5,6},
			   {0,6,7,8,0,2,3,4,5},{0,5,6,7,8,0,0,3,4}};

double CChain::Chain_Area( int N, double dScaleX, double dScaleY)
{
 	 int FirstValue=0,PrevValue=0, CurrValue;
	 double  Area=0, ExceptedArea=0;
	 int number;
     int *BPixel;      

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 // 1번 픽셀과 마지막 픽셀을 비교해서 1번 픽셀의 검색값 과 면적 구함
	 if(BPixel[0] > BPixel[2*number-2])
	 {
		 if(BPixel[1]>BPixel[2*number-1])     { PrevValue=5; Area+=BPixel[1]-0.5;}
		 else if(BPixel[1]<BPixel[2*number-1]){ PrevValue=7; Area+=BPixel[1]+0.5;}
		 else                                 { PrevValue=6; Area+=BPixel[1];}
	 }
	 else if(BPixel[0] < BPixel[2*number-2])
	 {
		 if(BPixel[1]>BPixel[2*number-1])     { PrevValue=3; Area-=BPixel[1]-0.5;}
		 else if(BPixel[1]<BPixel[2*number-1]){ PrevValue=1; Area-=BPixel[1]+0.5;}
		 else                                 { PrevValue=2; Area-=BPixel[1];}
	 }
	 else
	 {
		 if(BPixel[1]>BPixel[2*number-1])      PrevValue=4;
		 else if(BPixel[1]<BPixel[2*number-1]) PrevValue=8;
		 else   
			 exit(0);
	 }

	 FirstValue=PrevValue; // 1번 픽셀의 검색값을 FirstValue에 미리 저장

	 // 2번 픽셀부터 마지막 픽셀까지 검색값과 면적 구함
	 for(int i=1;i<number;i++)
	 {
		if(BPixel[2*i] > BPixel[2*i-2])
		{
		  if(BPixel[2*i+1]>BPixel[2*i-1])      { CurrValue=5; Area+=BPixel[2*i+1]-0.5;}
		  else if(BPixel[2*i+1]<BPixel[2*i-1]) { CurrValue=7; Area+=BPixel[2*i+1]+0.5;}
		  else                                 { CurrValue=6; Area+=BPixel[2*i+1];}
		}
		else if(BPixel[2*i] < BPixel[2*i-2])
		{
		  if(BPixel[2*i+1]>BPixel[2*i-1])      { CurrValue=3; Area-=BPixel[2*i+1]-0.5;}
		  else if(BPixel[2*i+1]<BPixel[2*i-1]) { CurrValue=1; Area-=BPixel[2*i+1]+0.5;}
		  else                                 { CurrValue=2; Area-=BPixel[2*i+1];}
		}
		else
		{
		  if(BPixel[2*i+1]>BPixel[2*i-1])      CurrValue=4;
		  else if(BPixel[2*i+1]<BPixel[2*i-1]) CurrValue=8;
		  else   
			  exit(0);
		}

		// 짤려진 면적을 여기서 보상해줌
		ExceptedArea+=L[PrevValue][CurrValue];   // 1번 부터 number-1 까지.

		PrevValue=CurrValue;		

	 }

	 ExceptedArea+=L[PrevValue][FirstValue]; // number 째 면적 보상
	 ExceptedArea = ExceptedArea/8.0;

	 Area+= ExceptedArea;

	 Area = Area * dScaleX * dScaleY;

	 return Area;
}

double CChain::Chain_LogicalArea( int N)
{
	 double  Area=0;
	 int number;
     int *BPixel;      

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 // 1번 픽셀과 마지막 픽셀을 비교해서 1번 픽셀의 검색값 과 면적 구함
	 if(BPixel[0] > BPixel[2*number-2])
	 {
		 if(BPixel[1]>BPixel[2*number-1])     Area+=BPixel[1]-0.5;
		 else if(BPixel[1]<BPixel[2*number-1])Area+=BPixel[1]+0.5;
		 else                                 Area+=BPixel[1];
	 }
	 else if(BPixel[0] < BPixel[2*number-2])
	 {
		 if(BPixel[1]>BPixel[2*number-1])     Area-=BPixel[1]-0.5;
		 else if(BPixel[1]<BPixel[2*number-1])Area-=BPixel[1]+0.5;
		 else                                 Area-=BPixel[1];
	 }
	 
	 // 2번 픽셀부터 마지막 픽셀까지 검색값과 면적 구함
	 for(int i=1;i<number;i++)
	 {
		if(BPixel[2*i] > BPixel[2*i-2])
		{
		  if(BPixel[2*i+1]>BPixel[2*i-1])      Area+=BPixel[2*i+1]-0.5;
		  else if(BPixel[2*i+1]<BPixel[2*i-1]) Area+=BPixel[2*i+1]+0.5;
		  else                                 Area+=BPixel[2*i+1];
		}
		else if(BPixel[2*i] < BPixel[2*i-2])
		{
		  if(BPixel[2*i+1]>BPixel[2*i-1])      Area-=BPixel[2*i+1]-0.5;
		  else if(BPixel[2*i+1]<BPixel[2*i-1]) Area-=BPixel[2*i+1]+0.5;
		  else                                 Area-=BPixel[2*i+1];
		}
	
	 }

	 return Area;
}

// Chain의 길이를 구한다. 대각선의 경계길이는 1.414로 계산하였다.
double CChain::Chain_Length(int N, double dScaleX, double dScaleY)
{
	double dLength = 0;
	int number;
	int *BPixel;

	double dDiagonal = sqrt(pow(dScaleX, 2) + pow(dScaleY, 2));
	dDiagonal = std::min(dScaleX, dScaleY);
	//dDiagonal = (dScaleX+ dScaleY)/2; // 

	number = m_chain[N];
	BPixel = &m_chaindata[m_chainstart[N]];

	for (int i = 0; i < number - 1; i++) {
		if (BPixel[2 * i] != BPixel[2 * i + 2] && BPixel[2 * i + 1] != BPixel[2 * i + 3])
			dLength += dDiagonal;
		else if (BPixel[2 * i] != BPixel[2 * i + 2]) dLength += dScaleX;
		else  dLength += dScaleY;
	}

	if (BPixel[0] != BPixel[2 * number - 2] && BPixel[1] != BPixel[2 * number - 1])
		dLength += dDiagonal;
	else if (BPixel[0] != BPixel[2 * number - 2]) dLength += dScaleX;
	else dLength += dScaleY;

	return dLength;
}



int CChain::Chain_Center( int N, double *cx, double *cy)
{
	int i, FirstValue = 0, PrevValue = 0, CurrValue;
	double XSum, YSum, LineSum, Area;
	double x, y, xExcept, yExcept;
	int number;
	int *BPixel;

	number = m_chain[N];
	BPixel = &m_chaindata[m_chainstart[N]];

	//BEGIN-0 : x의 센터와 Area를 구한다.>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>

	LineSum = 0;
	XSum = 0;
	Area = 0;
	x = (BPixel[0] + BPixel[2 * number - 2]) / 2.0;
	if (BPixel[0] > BPixel[2 * number - 2])
	{
		if (BPixel[1] > BPixel[2 * number - 1]) { PrevValue = 5; LineSum = (BPixel[1] - 0.5); }
		else if (BPixel[1] < BPixel[2 * number - 1]) { PrevValue = 7; LineSum = (BPixel[1] + 0.5); }
		else { PrevValue = 6; LineSum = (BPixel[1]); }
	}
	else if (BPixel[0] < BPixel[2 * number - 2])
	{
		if (BPixel[1] > BPixel[2 * number - 1]) { PrevValue = 3; LineSum = -(BPixel[1] - 0.5); }
		else if (BPixel[1] < BPixel[2 * number - 1]) { PrevValue = 1; LineSum = -(BPixel[1] + 0.5); }
		else { PrevValue = 2; LineSum = -(BPixel[1]); }
	}
	else
	{
		if (BPixel[1] > BPixel[2 * number - 1])      PrevValue = 4;
		else if (BPixel[1] < BPixel[2 * number - 1]) PrevValue = 8;
		else
			exit(0);
	}

	Area = LineSum;
	FirstValue = PrevValue; // 1번 픽셀의 검색값을 FirstValue에 미리 저장
	XSum = x*LineSum;

	for (i = 1; i < number; i++)
	{
		x = (BPixel[2 * i] + BPixel[2 * i - 2]) / 2.0;
		LineSum = 0;
		if (BPixel[2 * i] > BPixel[2 * i - 2])
		{
			if (BPixel[2 * i + 1] > BPixel[2 * i - 1]) { CurrValue = 5; LineSum = (BPixel[2 * i + 1] - 0.5); }
			else if (BPixel[2 * i + 1] < BPixel[2 * i - 1]) { CurrValue = 7; LineSum = (BPixel[2 * i + 1] + 0.5); }
			else { CurrValue = 6; LineSum = (BPixel[2 * i + 1]); }
		}
		else if (BPixel[2 * i] < BPixel[2 * i - 2])
		{
			if (BPixel[2 * i + 1] > BPixel[2 * i - 1]) { CurrValue = 3; LineSum = -(BPixel[2 * i + 1] - 0.5); }
			else if (BPixel[2 * i + 1] < BPixel[2 * i - 1]) { CurrValue = 1; LineSum = -(BPixel[2 * i + 1] + 0.5); }
			else { CurrValue = 2; LineSum = -(BPixel[2 * i + 1]); }
		}
		else
		{
			if (BPixel[2 * i + 1] > BPixel[2 * i - 1])      CurrValue = 4;
			else if (BPixel[2 * i + 1] < BPixel[2 * i - 1]) CurrValue = 8;
			else
				exit(0);
		}

		Area += LineSum;
		XSum += x*LineSum;

		// 짤려진 면적을 여기서 보상해줌
		Area += L[PrevValue][CurrValue] / 8.0;
		xExcept = BPixel[2 * i - 2];
		XSum += xExcept*L[PrevValue][CurrValue] / 8.0;   // 1번 부터 number-1 까지.

		PrevValue = CurrValue;

	}

	Area += L[PrevValue][FirstValue] / 8.0;  // number 째 면적 보상  
	xExcept = BPixel[2 * number - 2];
	XSum += xExcept*L[PrevValue][FirstValue] / 8.0; // number 째 면적 보상     

	*cx = XSum / Area;
	//END-0 <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<

	//BEGIN-0 : Y의 센터를 구한다. >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>

	YSum = 0;
	y = (BPixel[1] + BPixel[2 * number - 1]) / 2.0;
	if (BPixel[1] > BPixel[2 * number - 1])
	{
		if (BPixel[0] > BPixel[2 * number - 2]) { PrevValue = 5; YSum -= y*(BPixel[0] - 0.5); }
		else if (BPixel[0] < BPixel[2 * number - 2]) { PrevValue = 3; YSum -= y*(BPixel[0] + 0.5); }
		else { PrevValue = 4; YSum -= y*(BPixel[0]); }
	}
	else if (BPixel[1] < BPixel[2 * number - 1])
	{
		if (BPixel[0] > BPixel[2 * number - 2]) { PrevValue = 7; YSum += y*(BPixel[0] - 0.5); }
		else if (BPixel[0] < BPixel[2 * number - 2]) { PrevValue = 1; YSum += y*(BPixel[0] + 0.5); }
		else { PrevValue = 8; YSum += y*(BPixel[0]); }
	}
	else
	{
		if (BPixel[0] > BPixel[2 * number - 2])      PrevValue = 6;
		else if (BPixel[0] < BPixel[2 * number - 2]) PrevValue = 2;
		else
			exit(0);
	}

	FirstValue = PrevValue; // 1번 픽셀의 검색값을 FirstValue에 미리 저장


	for (i = 1; i < number; i++)
	{
		y = (BPixel[2 * i + 1] + BPixel[2 * i - 1]) / 2.0;
		if (BPixel[2 * i + 1] > BPixel[2 * i - 1])
		{
			if (BPixel[2 * i] > BPixel[2 * i - 2]) { CurrValue = 5; YSum -= y*(BPixel[2 * i] - 0.5); }
			else if (BPixel[2 * i] < BPixel[2 * i - 2]) { CurrValue = 3; YSum -= y*(BPixel[2 * i] + 0.5); }
			else { CurrValue = 4; YSum -= y*(BPixel[2 * i]); }

		}
		else if (BPixel[2 * i + 1] < BPixel[2 * i - 1])
		{
			if (BPixel[2 * i] > BPixel[2 * i - 2]) { CurrValue = 7; YSum += y*(BPixel[2 * i] - 0.5); }
			else if (BPixel[2 * i] < BPixel[2 * i - 2]) { CurrValue = 1; YSum += y*(BPixel[2 * i] + 0.5); }
			else { CurrValue = 8; YSum += y*(BPixel[2 * i]); }
		}
		else
		{
			if (BPixel[2 * i] > BPixel[2 * i - 2])      CurrValue = 6;
			else if (BPixel[2 * i] < BPixel[2 * i - 2]) CurrValue = 2;
			else
				exit(0);
		}

		// 짤려진 면적을 여기서 보상해줌
		yExcept = BPixel[2 * i - 1];
		YSum += yExcept*L[PrevValue][CurrValue] / 8.0;   // 1번 부터 number-1 까지.

		PrevValue = CurrValue;
	}

	yExcept = BPixel[2 * number - 1];
	YSum += yExcept*L[PrevValue][FirstValue] / 8.0; // number 째 면적 보상      //

	*cy = YSum / Area;

	 return 0;
}

int CChain::FindMaxX(int N)
{
	 int  i,Data;
	 int number;
     int *BPixel;      

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 Data=BPixel[0];
     for(i=1;i<number;i++)
		 if(BPixel[2*i]>Data) Data=BPixel[2*i];

	 return Data;
}

int CChain::FindMinX(int N)
{
	 int  i,Data;
	 int number;
     int *BPixel;      

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 Data=BPixel[0];
     for(i=1;i<number;i++)
		 if(BPixel[2*i]<Data) Data=BPixel[2*i];

	 return Data;
}
int CChain::FindMaxY(int N)
{
	 int  i,Data;
	 int number;
     int *BPixel;      

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 Data=BPixel[1];
     for(i=1;i<number;i++)
		 if(BPixel[2*i+1]>Data) Data=BPixel[2*i+1];

	 return Data;
}
int CChain::FindMinY(int N)
{
	 int  i,Data;
	 int number;
     int *BPixel;      

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 Data=BPixel[1];
     for(i=1;i<number;i++)
		 if(BPixel[2*i+1]<Data) Data=BPixel[2*i+1];

	 return Data;
}


// 중심에대한 모먼트를 구한다.
double CChain::FindMoment(int N)
{
	 double cx,cy;
     Chain_Center( N, &cx, &cy);
     return FindMoment(N, cx, cy);
}
double CChain::FindMoment(int N, double cx, double cy)
{
	 int number;
     int *BPixel;      
	 double dMoment;

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

     dMoment=0;
	 for(int i=0;i<number;i++)
		 dMoment+=(BPixel[2*i]-cx)*(BPixel[2*i]-cx)+(BPixel[2*i+1]-cy)*(BPixel[2*i+1]-cy);

	 return (dMoment/number);
}


double CChain::FindCompactness(int N, double dScaleX, double dScaleY)
{
	 double dCompactness;
	 double dLength;       
	 double dArea;

	 dLength = Chain_Length(N, dScaleX, dScaleY);
	 dArea   = Chain_Area(N, dScaleX, dScaleY);
	 dCompactness=(4*PI*dArea)/(dLength*dLength);

     if(dCompactness>1.0) dCompactness=1.0;

	 return dCompactness;
}

// angle = atan2( 2*(M00*M11-M10*M01),(M00*M20-M10*M10)-(M00*M02-M01*M01))*90/3.141592;
// angle은 degree이다.
double CChain::FindAngle(int N)
{
	 int number;
     int *BPixel; 
     double M00, M11, M10, M01, M20, M02;
	 double dAngle;

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]];

	 M00=M11=M10=M01=M20=M02=0;
	 for(int i=0;i < number ;i++)
     {
		 M00++;
		 M10+=(BPixel[2*i]);
		 M01+=(BPixel[2*i+1]);
		 M11+=(BPixel[2*i])*(BPixel[2*i+1]);
		 M20+=(BPixel[2*i])*(BPixel[2*i]);
		 M02+=(BPixel[2*i+1])*(BPixel[2*i+1]);
     }
	 dAngle = atan2(2*(M00*M11-M10*M01),(M00*M20-M10*M10)-(M00*M02-M01*M01))*90/PI;

	 return  dAngle;
}
int CChain::FineDistFromPoint(int N, double cx, double cy, double *min, double *max, double *avg)
{
	 int i, number;
     int *BPixel;
	 double dist, sum;

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]];
	
	 dist=sqrt((BPixel[0]-cx)*(BPixel[0]-cx)+(BPixel[1]-cy)*(BPixel[1]-cy));
	 *min=*max=sum=dist;
	 for(i=1;i<number;i++)
	 {
		 dist=sqrt((BPixel[2*i]-cx)*(BPixel[2*i]-cx)+(BPixel[2*i+1]-cy)*(BPixel[2*i+1]-cy));
		 if(*min<dist) *min=dist;
		 if(*max>dist) *max=dist;
		 sum+=dist;
	 }
	 *avg=sum/number;

	return 0;
}
int CChain::GetLeft_RightBoundary(int N, int YValue, int *left, int *right)
{
	 int i, number;
     int *BPixel;

	 number = m_chain[N];
     BPixel = &m_chaindata[m_chainstart[N]]; 

	 *left=WIDTH_MEM-1;
	 *right=0;
	for(i=0;i<number;i++)
	{
		if(BPixel[2*i+1]==YValue)
		{
			if(BPixel[2*i]<*left) *left=BPixel[2*i];
			if(BPixel[2*i]>*right) *right=BPixel[2*i];
		}
	}
	return 0;
}

//-----------------------------------------------------------------------------
// Yoon, Jung Hwan - proceed below
//-----------------------------------------------------------------------------

// chain의 중심을 지나는 장축과 단축의 직선의 방정식
// 장축선의 chain점
/*
void CChain::Get_MajorAxis(int *x1, int *y1, int x2, int y2)
{
	double angle = FindAngle(N);
}

void CChain::Get_MinorAxis(int *x1, int *y1, int x2, int y2)
{
}
*/
//-----------------------------------------------------------------------------


